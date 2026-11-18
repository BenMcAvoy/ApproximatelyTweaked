# ApproximatelyTweaked

ApproximatelyTweaked is a C# MelonLoader mod experiment for *Approximately Up*. It calculates how many components remain available and draws those counts beside the in-game hand component list.

## Build notes

The project targets .NET Standard 2.1 and references the game's Unity assemblies. The project file currently points to assemblies under the Steam installation path for the Approximately Up demo; update those `HintPath` values for your local install before building the solution in Visual Studio.

## Status

This is a small game-specific quality-of-life mod. It depends on the game's internal types and may need changes when the game updates.
