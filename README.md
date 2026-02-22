# Server Monitor Plugin

A lightweight C# plugin for Rust (compatible with both **Oxide** and **Carbon**) designed to broadcast your server's performance metrics in real-time straight to your web dashboard.

## Installation

1. Drop the `ServerMonitor.cs` file into your Oxide or Carbon `plugins` folder.
2. The server will automatically compile it and generate a configuration file at `/oxide/config/ServerStats.json`.

## Configuration

The first time the plugin is loaded, it will automatically generate a **Random Password**. You must use this password to authenticate your Server Dashboard on the website.

Default Configuration File (`oxide/config/ServerStats.json`):
```json
{
  "ApiUrl": "https://rustcenter.org/api/server-monitor/ingest",
  "Password": "YOUR_GENERATED_PASSWORD_HERE"
}
```

- **ApiUrl**: The API route of your website (e.g., Vercel) where the metrics will be sent to. The default is set to `rustcenter.org`.
- **Password**: An auto-generated token that acts as a secure key, proving to the website that the incoming statistics genuinely belong to your server.

## How it works (Smart Sleep Mode)

To prevent overloading the dashboard and your Rust server's resources, the plugin features a built-in **Sleep Mode**.
If it detects that no one currently has the web dashboard open in their browser, the plugin will seamlessly enter sleep mode, drastically reducing the tick updates to once every minute. As soon as an administrator opens the dashboard, the plugin instantly "wakes up" and resumes flashing fluid updates on the screen.

<br>

---

## ❤️ Keep the Project Alive!

If my work helps your server grow, please consider supporting the development! I dedicate a lot of free time to create optimized, low-lag plugins for the Rust community. 

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/T6T4WW645)

- **GitHub Sponsors:** [@DeltaDinizzz](https://github.com/sponsors/DeltaDinizzz)

## 💬 Support & Community

Need help configuring the monitor? Found a bug? Just wanna chat? 
Join the community on Discord!

[![Discord](https://img.shields.io/badge/Discord-Join_Community-7289da?style=for-the-badge&logo=discord&logoColor=white)](https://discord.gg/9WUSm5s6es)
