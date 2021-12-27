# ElectroLock (Remod original)

Adds an associated code/key lock underneath a placed electrical switch or on a fuel generator.  This feature can be enabled or disabled to facilitate the placement of switches or generators normally without the lock.  The lock code must be set and the lock locked to lock the switch or generator.
![](https://i.imgur.com/Qs2wXkK.jpg)

On first use, the player must enable first via the chat command /el on.  This setting will remain until the player enters /el off.

Switches or generators are placed normally via the Rust user interface.  Be sure to leave enough room below switches for the lock.

When the user attempts to pickup the lock, access will be denied.  However, they can pickup the switch or generator as they would normally, and the lock will also be removed.   A player cannot pickup or toggle a locked switch or generator.

## Configuration

```json
{
  "Settings": {
    "Owner can bypass lock": false,
    "Use key lock instead of code lock": false,
    "debug": false
  },
  "Version": {
    "Major": 1,
    "Minor": 1,
    "Patch": 3
  }
}
```

  - `Owner can bypass lock` - If true, the owner of the locked switch or generator can open or toggle it without unlocking first.
  - `Use key lock instead of code lock` -- Spawns a key lock instead of a code lock

## Permissions

- `electrolock.use` -- Allows player to placed a locked switch
- `electrolock.admin` -- Placeholder for future use

## Chat Commands

- `/el` -- Display enable/disable status with instructions
- `/el on` -- Enable locked switch/generator placement (switch with an associated lock)
- `/el off` -- Disable locked switch/generator placement (standard behavior without lock)
- `/el who` -- Allows user with admin permission above to see who owns the switch they are looking at
