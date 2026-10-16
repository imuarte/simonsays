# openkogama-simonsays

Simon Says for [OpenKogama](https://github.com/openkogama/openkogama-server) servers.

- `!simonsays` joins the lobby
- `!simonsays start [round]` starts the game, `!simonsays stop` ends it
- `!simonsays config` shows the settings of the world (`min`, `state`, `plate`, `night`)

## Build

Clone next to [openkogama-server](https://github.com/openkogama/openkogama-server), run `dotnet build -c Release`, copy `bin/Release/net10.0/simonsays.dll` into the server's `plugins` folder and type `!reload`.
