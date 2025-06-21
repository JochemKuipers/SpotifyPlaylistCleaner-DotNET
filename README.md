# Spotify Playlist Cleaner (.NET)

A simple application to clean up your Spotify playlists by removing duplicate tracks, finding missing tracks, and more.

## Features

- Remove duplicate tracks from playlists
- Identify missing tracks
- Sort playlist by various criteria
- Cross-platform support (Windows, macOS, Linux)

## Installation

1. Clone the repository
```bash
git clone https://github.com/yourusername/SpotifyPlaylistCleaner-DotNET.git
```

2. Navigate to the project directory
```bash
cd SpotifyPlaylistCleaner-DotNET
```

3. Build the application
```bash
dotnet build
```

## Usage

1. Run the application
```bash
dotnet run
```

2. Authenticate with your Spotify account
3. Select the playlist you want to clean
4. Choose the cleaning operations to perform
5. Review and save changes

## Requirements

- .NET 9.0 or higher
- Spotify account
- Spotify Developer API credentials

## Configuration

The application will prompt you for your Spotify Client ID on first run. 

To get your Client ID:
1. Go to the [Spotify Developer Dashboard](https://developer.spotify.com/dashboard/)
2. Log in with your Spotify account
3. Create a new application
4. Set the redirect URI to `http://127.0.0.1:3000/callback`
5. Copy the Client ID to use in the application

Your credentials will be securely stored locally for future use.

## License

MIT

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.