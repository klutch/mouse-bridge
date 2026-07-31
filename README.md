# Mouse Bridge
Improved mouse movement around boundary conditions for Windows 10 & 11.
*Warning: Not widely tested. It's just a tool I use personally.*

## Problem
Windows 10/11 has an annoying feature / bug with multiple monitor setups where the cursor gets snagged on corner, preventing the mouse from moving to another monitor unless care is taken to avoid corners. Another issue is warping to discontinuous positions on the other monitor if the cursor accidentally grazes/crosses the wrong edge of the screen.

![A poorly-behaved corner](example_problem.gif)<br/>
*Example of snagging and warping.*

## Solution

This program runs in the system tray and addresses both problems by hooking into the mouse movement event. It checks to see if the desktop topology (all screen bounding rects) contains the point where the mouse cursor wants to be. If not, the cursor's position is clamped to remain inside the topology and the event is suppressed.

![A well-behaved corner](example_solution.gif)<br/>
*Expected movement.*

### Code
The main functionality of the program is this, plus a lot of boilerplate:
```
if (!_topology.Contains(data.pt))
{
  Cursor.Position = _topology.Clamp(data.pt).ToPoint();
  return 1;
}
```