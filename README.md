FarmAnimalOwnership is a Synthesis patcher that assigns ownership to animals based on inclusion, exclusion, and internal logic.
The patcher comes configurable through the Synthesis UI and prints out a fairly detailed description
on the who, what, where, and why of the patching.

The basic logic of the patcher is to look for farm animals (e.g. goats, dogs, and chickens). Check if they have an owner.
Check if the location/cell has a matching faction or a matching town-faction (e.g. towns, farms, and mills and locations with a town in their name). Then assign ownership accordingly. 
For example: if the patcher can't match the chicken at farm x with a faction connected to the farm, it will instead try matching the chicken to the faction of town y. And if that fails there are some fallbacks in place. Like manually input matches, plugins with a town in their name, and what faction owns the other present animals.

The patcher is not flawless and it is likely to miss some, and maybe even patch some that shouldn't be. The reason the patcher works as well as it does
is because the inclusion and exclusion rules narrow the candidates before ownership is assigned.
With that said, in my personal load order with 4000 mods, the patcher found over 600 animals to assign ownership to. *Chefs kiss*

## Matching modes

These two modes apply to race, base NPC, owner, plugin filename, cell, and location type keyword matches.

- **Contains match:** The checked field contains the entered value anywhere, ignoring case. "**Cow**" matches "Rorikstead**Cow**". This can catch unintended races too: "**Cock**" matches "mihail**cock**atricerace2".
- **Wildcard match:** Unlike Contains match, the entire checked field must fit the pattern, ignoring case. "**\***" matches zero or more characters; "**?**" matches exactly one character. "**\*Cow**" matches "**Rorikstead**Cow"; the "\*" covers "Rorikstead". "**Cow\***" does not match "RoriksteadCow", but it matches "Cow" (zero extra characters) and "Cow**01**". "**\*Cock**" does not match "mihailcockatricerace2" because that ID does not end in Cock. "**RoriksteadCo?**" matches "RoriksteadCo**w**"; "**RoriksteadCo??**" does not.

An entry uses Wildcard match when it contains an asterisk or question mark. Otherwise it uses Contains match, even if you enter a complete EditorID.

## Values the patcher checks

An EditorID is an internal record identifier, not an in-game display name.

| Value | Meaning |
| --- | --- |
| Race EditorID | The animal's race record ID. |
| Base NPC EditorID | The animal's base actor record ID, shared by its placed instances. |
| Owner EditorID | The current owner record ID on an already-owned animal; the owner may be a faction or NPC. |
| Plugin filename | The `.esp`, `.esm`, or `.esl` file supplying the winning placed-animal record. |
| Cell EditorID | The ID of the cell containing the placed animal. |
| Location EditorID | The ID of the location linked to that cell. |
| LocType keyword EditorID | A `LocType`-prefixed keyword ID on the location, such as `LocTypeDungeon`. |
| Faction EditorID | The ID of a faction considered as an owner. |

## Rule order

The first matching exclusion stops the animal from being patched. The first owner found is assigned; later owner rules are not checked.

1. Check **Race EditorID matches**. If the race does not match, stop.
2. If the animal already has an owner, leave it unchanged. Its owner counts toward the cell's vote unless **Owner EditorID matches excluded from voting** applies.
3. Check **Cell EditorID matches to exclude**.
4. Check **LocType keyword EditorID matches to exclude**.
5. Check **Plugin filename matches to exclude**.
6. Check **Base NPC EditorID matches to exclude**.
7. Try a faction from the animal's plugin that matches the cell or location EditorID.
8. Try a faction matching the cell or location EditorID.
9. Try **Manual faction matches**: exact match first, then partial match.
10. Try **Plugin faction fallback**.
11. Try the ownership vote if it meets **Minimum owned animals for ownership vote**. If no owner is found, skip the animal.
