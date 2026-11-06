// Runs only in the settings window — the main (remote) window has no Tauri IPC at all.
// Plain JS on purpose: this page ships as static frontendDist with no build step, and the
// Tauri API arrives via withGlobalTauri instead of a bundled @tauri-apps/api import.
"use strict";

const { invoke } = window.__TAURI__.core;

const form = document.getElementById("settings-form");
const input = document.getElementById("server-url");
const feedback = document.getElementById("feedback");
const resetButton = document.getElementById("reset");
const defaultUrl = document.getElementById("default-url");

function render(settings) {
  input.value = settings.serverUrl;
  defaultUrl.textContent = settings.defaultServerUrl;
  resetButton.disabled = !settings.isOverride;
}

function showFeedback(message, isError) {
  feedback.textContent = message;
  feedback.classList.toggle("error", isError);
}

form.addEventListener("submit", async (event) => {
  event.preventDefault();
  showFeedback("", false);
  try {
    render(await invoke("set_server_url", { url: input.value }));
    showFeedback("Saved — the main window is loading the new server.", false);
  } catch (error) {
    showFeedback(String(error), true);
  }
});

resetButton.addEventListener("click", async () => {
  showFeedback("", false);
  try {
    render(await invoke("reset_server_url"));
    showFeedback("Reset — the main window is loading the built-in default.", false);
  } catch (error) {
    showFeedback(String(error), true);
  }
});

invoke("get_settings")
  .then(render)
  .catch((error) => showFeedback(String(error), true));
