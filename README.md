# moms-bathroom

A walk-through VR model of the bathroom renovation for Meta Quest, built from the client's drawings.
The project is set up following the LAVA "Quest Tutorials 2026" Part A and Part C.

## Run it on the Quest
1. Plug the Quest in over USB-C and allow the computer from inside the headset (Part B).
2. Either open the project in Unity (Unity Hub > Add > this folder), then File > Build Profiles > Android > Build And Run,
   or install the prebuilt APK:
   `/Applications/Unity/Hub/Editor/6000.5.10f1/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb install -r Builds/MomsBathroom.apk`
3. In the headset: Library > Unknown Sources > Moms Bathroom.

You start just inside the door, facing north. The room is true scale, so you can walk it physically.
- Right thumbstick forward: aim and teleport (floor and pebble shower floor). Left or right: snap turn.

## Editing the design
All dimensions are constants at the top of `Assets/Bathroom/Editor/BathroomBuilder.cs`, in inches
(x = east from the west wall, z = north from the south wall, y = up).
- Tools > Moms Bathroom > Build Scene (room + XR rig + lighting): rebuilds everything and re-bakes the lighting.
- Tools > Moms Bathroom > Render Review Snapshots: writes stills to `Builds/Snapshots`.
- Tools > Moms Bathroom > Build Quest APK.
- Tools > Quest Setup > Run Part A Setup: reapplies the Part A project setup (also used for the starter zip).

## Materials
- Tile: Atlas Concorde Boost Pro Cream, Taupe and Tobacco official texture images, laid as 120 x 120 cm large format.
- Vanity: Room & Board Emerson 72" double, walnut. Front textures and proportions come from the product photo.
- Floor (whole room, including the shower): Architessa Level Pebble sheet photo, 12" sheets.
- Shower niche: Boost Pro Cream.
- Tub: MTI Andrea 1 undermount (71-5/8 x 31-5/8 x 22-1/4); the 64" x 23.5" in the drawing is the deck cutout.

## Assumptions to confirm
- Ceiling height 8'-0" (not on the drawings).
- The cream rectangle drawn on the south wall above the vanity end is modeled as a cream tile panel.
- Vanity faucets (8" widespread) and the deck-mount tub filler are placeholders in chrome; models not specified.
- LED mirrors are centered on the actual Emerson sinks (20-1/4" either side of center).
- Eskimo Gordon finish: rendered as gunmetal. Eskimo's listings show white, dark grey and black.
