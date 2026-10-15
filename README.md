# openkogama-simonsays

Simon Says for [OpenKogama](https://github.com/openkogama/openkogama-server) servers. A player types `!simonsays` in chat and everyone in that world gets five rounds, in chat and as notifications: `/wave`, jump, drop your weapon, pick up a weapon, and don't touch the water. In the last one the world's water stays calm for 5 seconds, then rises 3 blocks (12 units) in 7.5 seconds, and whoever touches it loses. A world without water gets some just below the lowest player for that round.

## Build

Clone this repository next to [openkogama-server](https://github.com/openkogama/openkogama-server), install the .NET 10 SDK and run:

```sh
dotnet build -c Release
```

## Install

Copy `bin/Release/net10.0/simonsays.dll` into the `plugins` folder of the server's data folder and restart the server, or type `!reload` as an admin.
