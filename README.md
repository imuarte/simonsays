# openkogama-simonsays

Simon Says for [OpenKogama](https://github.com/openkogama/openkogama-server) servers. A player types `!simonsays` in chat and everyone gets the first task, `/wave`, in chat and as a notification.

## Build

Clone this repository next to [openkogama-server](https://github.com/openkogama/openkogama-server), install the .NET 10 SDK and run:

```sh
dotnet build -c Release
```

## Install

Copy `bin/Release/net10.0/simonsays.dll` into the `plugins` folder of the server's data folder and restart the server.
