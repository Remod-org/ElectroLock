# ElectroLock
Adds an associated code lock underneath a placed electrical switch.  This feature can be enabled or disabled to facilitate the placement of switches normally without the lock.  The lock code must be set and the lock locked to lock the switch.
![](https://i.imgur.com/Qs2wXkK.jpg)

On first use, the player must enable first via the chat command /el on.  This setting will remain until the player enters /el off.

Switches are placed normally via the Rust user interface.  Be sure to leave enough room below the switch for the lock.

When the user attempts to pickup the lock, access will be denied.  However, they can pickup the switch as they would normally, and the lock will also be removed.   A player cannot pickup or toggle a locked switch.

There is currently no configuration required for ElectroLock.  Data files are used to record placed switches and user enable status (one each).

## Permissions

- `electrolock.use` -- Allows player to placed a locked switch
- `electrolock.admin` --- Placeholder for future use

## Chat Commands

- `/el` -- Display enable/disable status with instructions
- `/el on` -- Enable locked switch placement (switch with an associated lock)
- `/el off` -- Disable locked switch placement (standard behavior without lock)
- `/el who` -- Allows user with admin permission above to see who owns the switch they are looking at
