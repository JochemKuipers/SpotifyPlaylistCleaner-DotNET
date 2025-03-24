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

Create a `.env` file in the project root directory with your Spotify API credentials:

```
SPOTIFY_CLIENT_ID=your-client-id
```

You'll need to obtain your Client ID from the [Spotify Developer Dashboard](https://developer.spotify.com/dashboard/).

## License

MIT

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.