//! Server-URL configuration: a build-time default (`FORUM_DESKTOP_SERVER_URL`, mirroring how
//! the frontend Docker image bakes `NEXT_PUBLIC_API_URL`) plus a runtime override persisted
//! in the OS config dir. The URL is the FRONTEND origin the webview navigates to — the SPA
//! served from it carries its own baked API/WS endpoints, so the shell needs exactly one knob.

use std::fs;
use std::io;
use std::path::PathBuf;
use std::sync::RwLock;

use serde::{Deserialize, Serialize};
use tauri::{AppHandle, Manager};
use url::Url;

pub const DEFAULT_SERVER_URL: &str = match option_env!("FORUM_DESKTOP_SERVER_URL") {
    Some(value) => value,
    None => "http://localhost:3000",
};

const SETTINGS_FILE: &str = "settings.json";

#[derive(Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
struct PersistedSettings {
    server_url: String,
}

struct Current {
    url: Url,
    is_override: bool,
}

pub struct AppState {
    settings_path: PathBuf,
    current: RwLock<Current>,
}

impl AppState {
    /// A missing or invalid settings file is never fatal — the shell falls back to the baked
    /// default so a broken override can always be corrected from the settings window.
    pub fn load(app: &AppHandle) -> tauri::Result<Self> {
        let settings_path = app.path().app_config_dir()?.join(SETTINGS_FILE);
        let mut current = Current { url: default_url(), is_override: false };

        match fs::read_to_string(&settings_path) {
            Ok(raw) => match serde_json::from_str::<PersistedSettings>(&raw)
                .map_err(|error| error.to_string())
                .and_then(|settings| validate_server_url(&settings.server_url))
            {
                Ok(url) => current = Current { url, is_override: true },
                Err(error) => {
                    eprintln!("ignoring invalid settings file {}: {error}", settings_path.display());
                }
            },
            Err(error) if error.kind() == io::ErrorKind::NotFound => {}
            Err(error) => {
                eprintln!("could not read settings file {}: {error}", settings_path.display());
            }
        }

        Ok(Self { settings_path, current: RwLock::new(current) })
    }

    pub fn server_url(&self) -> Url {
        self.current.read().expect("settings lock poisoned").url.clone()
    }

    pub fn is_override(&self) -> bool {
        self.current.read().expect("settings lock poisoned").is_override
    }

    /// Same-origin check for the navigation policy (scheme + host + effective port).
    pub fn is_allowed_origin(&self, url: &Url) -> bool {
        let allowed = self.server_url();
        url.scheme() == allowed.scheme()
            && url.host_str() == allowed.host_str()
            && url.port_or_known_default() == allowed.port_or_known_default()
    }

    pub fn set_override(&self, url: Url) -> io::Result<()> {
        if let Some(parent) = self.settings_path.parent() {
            fs::create_dir_all(parent)?;
        }
        let persisted = PersistedSettings { server_url: display_url(&url) };
        let json = serde_json::to_string_pretty(&persisted).expect("settings serialize cannot fail");
        fs::write(&self.settings_path, json)?;

        let mut current = self.current.write().expect("settings lock poisoned");
        *current = Current { url, is_override: true };
        Ok(())
    }

    /// Removes the override; returns the default URL now in effect.
    pub fn clear_override(&self) -> io::Result<Url> {
        match fs::remove_file(&self.settings_path) {
            Ok(()) => {}
            Err(error) if error.kind() == io::ErrorKind::NotFound => {}
            Err(error) => return Err(error),
        }
        let url = default_url();
        let mut current = self.current.write().expect("settings lock poisoned");
        *current = Current { url: url.clone(), is_override: false };
        Ok(url)
    }
}

fn default_url() -> Url {
    validate_server_url(DEFAULT_SERVER_URL).unwrap_or_else(|error| {
        eprintln!(
            "baked-in default server URL {DEFAULT_SERVER_URL:?} is invalid ({error}); \
             falling back to http://localhost:3000"
        );
        Url::parse("http://localhost:3000").expect("hardcoded fallback URL parses")
    })
}

/// Origin without the trailing slash `Url` adds — what the settings UI shows and the file stores.
pub fn display_url(url: &Url) -> String {
    url.as_str().trim_end_matches('/').to_string()
}

/// The only gate through which a URL can reach the webview: http(s) origin, nothing else.
/// The path rule mirrors the frontend Dockerfile's #1 footgun — a `/api` suffix would break
/// every request the SPA makes.
pub fn validate_server_url(input: &str) -> Result<Url, String> {
    let trimmed = input.trim();
    if trimmed.is_empty() {
        return Err("Enter a server URL.".into());
    }
    let url = Url::parse(trimmed).map_err(|error| format!("Not a valid URL: {error}."))?;
    if !matches!(url.scheme(), "http" | "https") {
        return Err("Only http:// and https:// URLs are allowed.".into());
    }
    if url.host_str().is_none() {
        return Err("The URL must include a host.".into());
    }
    if !url.username().is_empty() || url.password().is_some() {
        return Err("Credentials in the URL are not allowed.".into());
    }
    if url.query().is_some() || url.fragment().is_some() {
        return Err("The URL must not contain a query or fragment.".into());
    }
    if !url.path().is_empty() && url.path() != "/" {
        return Err(
            "Use the origin only (scheme://host[:port]) with no path — e.g. a trailing /api would break every request."
                .into(),
        );
    }
    Ok(url)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn accepts_plain_origins() {
        for input in ["http://localhost:3000", "https://forum.local", "https://forum.local/", " http://127.0.0.1:8080 "] {
            let url = validate_server_url(input).expect(input);
            assert!(matches!(url.scheme(), "http" | "https"));
        }
    }

    #[test]
    fn rejects_non_origins() {
        for input in [
            "",
            "forum.local",
            "ftp://forum.local",
            "javascript:alert(1)",
            "http://forum.local/api",
            "http://user:pass@forum.local",
            "http://forum.local/?q=1",
            "http://forum.local/#frag",
        ] {
            assert!(validate_server_url(input).is_err(), "should reject {input:?}");
        }
    }

    #[test]
    fn display_url_drops_the_trailing_slash() {
        let url = validate_server_url("https://forum.local").unwrap();
        assert_eq!(display_url(&url), "https://forum.local");
    }
}
