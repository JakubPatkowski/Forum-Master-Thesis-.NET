//! Thin native shell around the hosted forum SPA (ADR-level decision: hosted-URL mode — the
//! webview navigates to a running deployment exactly like a browser tab; nothing of the app
//! runs inside this process). Two windows:
//! - "main": the remote SPA. No Tauri capability, no IPC — a compromised page gets nothing
//!   from the shell that a browser tab would not have.
//! - "settings": a local bundled page (see `ui/`) for the server-URL override, the only
//!   window with IPC (see `capabilities/settings.json`).

mod commands;
mod config;

use tauri::menu::{MenuBuilder, MenuItemBuilder, SubmenuBuilder};
use tauri::{AppHandle, Manager, WebviewUrl, WebviewWindowBuilder};
use tauri_plugin_opener::OpenerExt;
use url::Url;

use config::AppState;

const MENU_SETTINGS: &str = "server-settings";
const MENU_RELOAD: &str = "reload";
const MENU_QUIT: &str = "quit";

/// Anchors with `target="_blank"` (markdown external links, presigned attachment links) would
/// otherwise ask the OS webview for a popup: WebKitGTK drops the request silently, WebView2
/// opens an unmanaged chromeless window. Rewriting them into same-window navigations funnels
/// everything through the one `on_navigation` policy — same-origin stays in the app, the rest
/// opens in the system browser.
const NEW_TAB_SHIM: &str = r#"
(function () {
  "use strict";
  window.open = function (url) {
    if (url) window.location.href = url;
    return null;
  };
  document.addEventListener(
    "click",
    function (event) {
      var anchor =
        event.target && event.target.closest ? event.target.closest('a[target="_blank"]') : null;
      if (anchor && anchor.href) {
        event.preventDefault();
        window.location.href = anchor.href;
      }
    },
    true
  );
})();
"#;

pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_opener::init())
        .invoke_handler(tauri::generate_handler![
            commands::get_settings,
            commands::set_server_url,
            commands::reset_server_url
        ])
        .setup(|app| {
            app.manage(AppState::load(app.handle())?);
            install_menu(app.handle())?;
            open_main_window(app.handle())?;
            Ok(())
        })
        .on_menu_event(|app, event| match event.id().as_ref() {
            MENU_SETTINGS => open_settings_window(app),
            MENU_RELOAD => reload_main_window(app),
            MENU_QUIT => app.exit(0),
            _ => {}
        })
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}

fn install_menu(app: &AppHandle) -> tauri::Result<()> {
    let settings = MenuItemBuilder::with_id(MENU_SETTINGS, "Server Settings…")
        .accelerator("CmdOrCtrl+Comma")
        .build(app)?;
    let reload = MenuItemBuilder::with_id(MENU_RELOAD, "Reload")
        .accelerator("CmdOrCtrl+R")
        .build(app)?;
    let quit = MenuItemBuilder::with_id(MENU_QUIT, "Quit")
        .accelerator("CmdOrCtrl+Q")
        .build(app)?;
    let forum_menu = SubmenuBuilder::new(app, "Forum")
        .item(&settings)
        .separator()
        .item(&reload)
        .separator()
        .item(&quit)
        .build()?;
    let menu = MenuBuilder::new(app).item(&forum_menu).build()?;
    app.set_menu(menu)?;
    Ok(())
}

fn open_main_window(app: &AppHandle) -> tauri::Result<()> {
    let server_url = app.state::<AppState>().server_url();
    let nav_handle = app.clone();
    WebviewWindowBuilder::new(app, "main", WebviewUrl::External(server_url))
        .title("Forum")
        .inner_size(1280.0, 800.0)
        .min_inner_size(480.0, 360.0)
        .initialization_script(NEW_TAB_SHIM)
        .on_navigation(move |url| decide_navigation(&nav_handle, url))
        .build()?;
    Ok(())
}

fn decide_navigation(app: &AppHandle, url: &Url) -> bool {
    // The webview's own blank bootstrap page.
    if url.scheme() == "about" {
        return true;
    }
    if app.state::<AppState>().is_allowed_origin(url) {
        return true;
    }
    if matches!(url.scheme(), "http" | "https") {
        if let Err(error) = app.opener().open_url(url.as_str(), None::<&str>) {
            eprintln!("failed to open {url} in the system browser: {error}");
        }
    }
    false
}

fn open_settings_window(app: &AppHandle) {
    if let Some(window) = app.get_webview_window("settings") {
        let _ = window.set_focus();
        return;
    }
    let result = WebviewWindowBuilder::new(app, "settings", WebviewUrl::App("index.html".into()))
        .title("Server Settings")
        .inner_size(560.0, 460.0)
        .build();
    if let Err(error) = result {
        eprintln!("failed to open the settings window: {error}");
    }
}

fn reload_main_window(app: &AppHandle) {
    if let Some(window) = app.get_webview_window("main") {
        let _ = window.eval("window.location.reload()");
    }
}

/// Point the main window at `url`, recreating the window if the user closed it.
pub(crate) fn show_main_at(app: &AppHandle, url: &Url) {
    match app.get_webview_window("main") {
        Some(window) => {
            if let Err(error) = window.navigate(url.clone()) {
                eprintln!("failed to navigate the main window: {error}");
            }
        }
        None => {
            if let Err(error) = open_main_window(app) {
                eprintln!("failed to reopen the main window: {error}");
            }
        }
    }
}
