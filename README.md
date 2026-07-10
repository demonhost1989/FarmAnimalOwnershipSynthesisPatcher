FarmAnimalOwnership is a Synthesis patcher that asigns ownership to animals based on inclusion and exclusion rules.
The patcher comes fully configurable through the Synthesis UI and prints out a fairly detailed description
on the who, what, and where of the patching.

The basic logic of the patcher is to look for farm animals (e.g. goats, dogs, and chickens) check if they have an owner,
check if the location/cell is civilized (e.g. towns, farms, and mills), check if there is a suitable owner, if not
there are some fallsbacks to try and asign a less specific ownership. For exmaple: if the patcher can't match
the chicken at farm x with a faction connected to the farm, it will instead try matching the chicken to the faction of town y.

The patcher is not flawless and it is likely to miss some, and maybe even patch some that shouldn't be. With that said, in my
personal load order with 4000 mods the patcher found over 500 animals to asign ownership to. *Chefs kiss*

The patcher comes preconfigured based on my personal load order and the default settings are viewable in the settings.cs file.
You can select races to patch (with partial matching), names to exclude (with partial matching), plugins to exclude (with wildcards),
cells to exclude (with wildcards), location types to exclude (exact matching), then there is a override section to
force a location/cell to use a particular faction for it's ownership patching.

A couple of notes on the printout report:
In the General Summary there is typically a big overlap between no suitable owner and an unsuitable location
since they can both be true and often are at the same time. In my testing they tended to be the same or
close to the same most of the time.
The animals excluded in the Exclusion Summary only displays animals that otherwise would have been patched.

To do:
One issue I have yet to crack is the possibly relevant animals in locations/cell without proper editorid data.
