# openkogama-simonsays

Simon Says for [OpenKogama](https://github.com/openkogama/openkogama-server) servers. `!simonsays` puts a player in the lobby, the red team. `!simonsays start` moves everyone in the world to the blue team and begins, `!simonsays start 6` or `!simonsays start colossus` begins from a given round, and `!simonsays stop` ends the game. For now every player can start, stop and set up the game. The players get six rounds, in chat and as notifications: `/wave`, jump, drop your weapon, pick up a weapon, don't touch the water, and kill the Colossus. Chat counts down the last 5, 3, 2 and 1 seconds of a round and after it names who is out, while "Done!" and "You're out!" go only to that player as notifications. At the end chat names the winners.

In the water round the world's water stays calm for 5 seconds, then rises 3 blocks (12 units) in 7.5 seconds, and whoever touches it loses. A world without water gets some just below the lowest player for that round. In the last round an npc Colossus turns its laser smoothly from one player to the next, skipping anyone behind cubes, sweeps it across the sky and now and then fires into it when nobody is in sight, and whoever is still in sight when it fires takes 40 damage; whoever the Colossus kills is out and goes back to the red team after respawning, and the round lasts until every Colossus is killed. A Colossus stands at every `$$colossus` marker (a Text Message cube with that text), or else at every Colossus trigger of the world, or else in the middle of the players, so the world needs no Colossus of its own. That round needs game versions 1.39 to 2.3, otherwise it is skipped. Whoever misses a task or touches the water is out for the rest of the game and goes back to the red team, and when the game ends the rest go back too. The world needs red and blue team spawns.

## Starting by itself

After `!simonsays config min 4` in a world, Simon Says runs there by itself: everyone who joins goes to the red team, chat shows how many players are in out of how many are needed, and once there are at least 4 the game starts in 10 seconds. If players leave before it ends, it stops and waits again. After a game there is a 5 second break and the lobby opens again.

`!simonsays config state 266001` ties a Toggle Box of that world to the game: it is off while Simon Says waits for players and on during a game, so the world's logic can follow it. Simon Says switches it itself, and when something else switches it on, the game starts right away, and off, the game stops. `!simonsays config plate 266166` names a Pressure Plate that switches that Toggle Box through the world's logic, for example through a Time Trigger, and then Simon Says steps on the plate for a moment instead of switching the box, so every game version follows the same logic.

`!simonsays config` shows the settings, `!simonsays config state off` forgets the Toggle Box, `!simonsays config plate off` the plate, and `!simonsays config off` stops starting by itself. The settings belong to one world at a time and are kept in `plugins/data/simonsays.json`.

## Build

Clone this repository next to [openkogama-server](https://github.com/openkogama/openkogama-server), install the .NET 10 SDK and run:

```sh
dotnet build -c Release
```

## Install

Copy `bin/Release/net10.0/simonsays.dll` into the `plugins` folder of the server's data folder and restart the server, or type `!reload` as an admin. It needs a server with `IWorld.Toggled`, `IWorld.Press` and `INpc.BecomeColossus(ColossusSize)`.
