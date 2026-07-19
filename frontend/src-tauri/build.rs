fn main() {
    // The baked-in default server URL is read via option_env! in src/config.rs; cargo only
    // re-runs the compile when this line marks the env var as an input.
    println!("cargo:rerun-if-env-changed=FORUM_DESKTOP_SERVER_URL");
    tauri_build::build()
}
