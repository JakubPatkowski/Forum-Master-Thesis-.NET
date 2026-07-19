//! IPC surface of the settings window — the only webview with any Tauri capability.

use serde::Serialize;
use tauri::{AppHandle, State};

use crate::config::{display_url, validate_server_url, AppState, DEFAULT_SERVER_URL};

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct SettingsDto {
    server_url: String,
    default_server_url: &'static str,
    is_override: bool,
}

fn current_dto(state: &AppState) -> SettingsDto {
    SettingsDto {
        server_url: display_url(&state.server_url()),
        default_server_url: DEFAULT_SERVER_URL,
        is_override: state.is_override(),
    }
}

#[tauri::command]
pub fn get_settings(state: State<'_, AppState>) -> SettingsDto {
    current_dto(&state)
}

#[tauri::command]
pub fn set_server_url(
    app: AppHandle,
    state: State<'_, AppState>,
    url: String,
) -> Result<SettingsDto, String> {
    let parsed = validate_server_url(&url)?;
    state
        .set_override(parsed.clone())
        .map_err(|error| format!("Could not save settings: {error}"))?;
    crate::show_main_at(&app, &parsed);
    Ok(current_dto(&state))
}

#[tauri::command]
pub fn reset_server_url(app: AppHandle, state: State<'_, AppState>) -> Result<SettingsDto, String> {
    let default = state
        .clear_override()
        .map_err(|error| format!("Could not reset settings: {error}"))?;
    crate::show_main_at(&app, &default);
    Ok(current_dto(&state))
}
