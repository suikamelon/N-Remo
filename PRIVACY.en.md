# Privacy

[日本語](PRIVACY.md) | English

N-Remo runs locally on the user's Windows environment as a desktop application.

## Information Handled by the Application

- Settings and credentials used to connect to Nature Remo, SwitchBot, and Tuya
- Registered buttons, commands, and simple automations
- Retrieved sensor values and sensor logs
- Connection information for Webhook relay services

This information is stored in a location selected by the user or in the user's Windows profile. Settings backups may include credentials.

## External Communication

Depending on the enabled features, N-Remo communicates with Nature Remo, SwitchBot, Tuya, Cloudflare, Firebase, and Google Apps Script. When the user performs an update check from the About tab, N-Remo communicates with the GitHub API to retrieve the latest version information.

N-Remo does not send usage information to analytics, advertising, or tracking servers operated by the author.

## User Control

Data and logs can be deleted from the settings screen. Backup files and data stored by external services must be deleted by the user from their respective storage locations.
