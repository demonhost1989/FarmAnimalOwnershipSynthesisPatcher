FarmAnimalOwnership is a Synthesis patcher that asigns ownership to animals based on inclusion, exclusion, and internal logic.
The patcher comes configurable through the Synthesis UI and prints out a fairly detailed description
on the who, what, where, and why of the patching.

The basic logic of the patcher is to look for farm animals (e.g. goats, dogs, and chickens). Check if they have an owner.
Check if the location/cell has a matching faction or a matching town-faction (e.g. towns, farms, and mills and locations with a town in their name). Then assign ownership accordingly. 
For exmaple: if the patcher can't match the chicken at farm x with a faction connected to the farm, it will instead try matching the chicken to the faction of town y. And if that fails there are some fallbacks in place. Like manually input matches, plugins with a town in their name, and what faction owns the other present animals.

The patcher is not flawless and it is likely to miss some, and maybe even patch some that shouldn't be. The reason the patcher works as well as it does
is because of the inclusing and exclusion that preempts the logic, so it will only ever aim to patch certain animals who are in appropriate locations. Or rather, it will not try and patch animals in dungeons, dwarven ruins, in the wilderness etc.
With that said, in my personal load order with 4000 mods, the patcher found over 600 animals to assign ownership to. *Chefs kiss*

The patcher comes preconfigured based on my personal load order and the default settings are viewable in the settings.cs file.
You can select races to patch (with partial matching), names to exclude (with partial matching), plugins to exclude (with partial matching),
cells to exclude (with partial matching), location types to exclude (with partial matching), then there is an manual input section to
match a location/cell with a particular faction.

Here's a simplified peek at what the patcher is looking for:
Animal races -> "Goat", "Chicken", "Cow", "Horse", "Pig", "Sheep", "Dog", "Cat", ETC.
Town factions -> "Riverwood", "OldHroldan", "Rorikstead", "Solitude", ETC.

and what the patcher is looking to avoid:
Animal names -> "Wild", "Stray", "Draugr", "Forsworn", "Bandit", "Pigeon", ETC.
Location Types -> "Dungeon", "AnimalDen", "Bandit", "DragonLair", "Draugr", "Dwarven", Falmer", "GiantCamp", ETC.
