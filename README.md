# Mouse Bridge
Improved mouse movement around boundary conditions for Windows.

## Problem
Windows 10/11 has a bug / annoying feature with multiple monitor setups where the cursor gets snagged on corners and warps to offset positions on the other monitor if it accidentally grazes the wrong edge of the screen.

![A poorly-behaved corner](example_problem.gif)

*Example of snagging and warping.*

## Solution

This program runs in the system tray and addresses both problems by hooking into the mouse movement event. It checks to see if the desktop topology (all screen bounding rects) contains the point where the mouse cursor wants to be. 

* If so, the cursor moves there. Fixes corner snagging.
* If not, the cursor's position is clamped to the topology to keep it inside. Lets the cursor slide along boundaries instead of warping to new positions on the other screen.

![A well-behaved corner](example_solution.gif)

*Expected movement.*