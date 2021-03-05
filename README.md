## Permissions

* `helicopterhover.canhover` -- Enables use of `/hover` chat command

## Chat  Commands

Use `/hover` while in driver seat to toggle hover.

Will display hover status in chat. ("Helicopter hover: enabled/disabled")

## Console Commands

* `helicopterhover.hover`

Bind  to a key with `bind <key> helicopterhover.hover`

## Configuration

```json
{
  "Broadcast message on mounted": true,
  "Permissions": {
    "Scraptranporthelicopter can hover": true,
    "Chinook can hover": true,
    "Enable hover with two occupants": true,
    "Passenger can toggle hover": true
  },
  "Hovering": {
    "Timed hover": false,
    "Timed hover duration": 60.0,
    "Use fuel while hovering": true,
    "Keep engine on when hovering": true,
    "Enable helicopter rotation on hover": true,
    "Disable hover on dismount": true,
    "Disable hover on change seats": false
  }
}
```

* **Scraptransporthelicopter can hover** - Can the scrap helicopter hover.
* **Chinook can hover** - Can the chinook hover.
* **Enable helicopter rotation on hover** - Enable player to steer minicopter with mouse while hovering.
* **Enable hover with two occupants** - Can the helicopter hover when there is a driver and a passenger. (If set to true, will allow hovering with any number of players, false will only allow hovering if one person is in the helicopter).
* **Keep engine on when hovering** - Keep engine on when switched to passenger seat and when dismounted (dismounting the passenger seat or dismounting any seat with disable hover on dismount set to false).
* **Disable hover on dismount** - If player jumps out of helicopter while hovering, does helicopter stay floating in the air or fall to the ground.
* **Use fuel while hovering** - Does the helicopter use fuel while hovering.
* **Passenger can toggle hover** - Can the passenger of the helicopter toggle the hover.
* **Helicopter hover duration** - How long (in seconds does the hover last for (just use a big number if you want it to last a long time).
* **Broadcast message on mounted** - Does the plugin broadcast the 'use /hover' message to players when they mount a helicopter.