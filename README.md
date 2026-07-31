# Mouse Bridge
Windows 10/11 has a bug / annoying feature with multiple monitor setups where the cursor gets snagged on corners and warp to offset positions on the other monitor if it accidentally grazes the wrong edge of the screen.

This program runs in the system tray and addresses both problems by hooking into the mouse movement event. It checks to see if the desktop topology (all screen bounding rects) contains the point where the mouse cursor wants to be. 

* If so, the cursor moves there. Fixes corner snagging.
* If not, the cursor's position is clamped to the topology to keep it inside. Lets the cursor slide along boundaries instead of warping to new positions on the other screen.