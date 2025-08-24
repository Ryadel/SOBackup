# SOBackup — A Safety Net for Unity ScriptableObjects

Stop losing data on refactors with a GUID‑based JSON backup, restore **and diff** workflow.

SOBackup is an Editor‑only utility to protect ScriptableObject data during schema changes. It backs up assets to pretty‑printed JSON (named by asset GUID) and restores values later via `EditorJsonUtility.FromJsonOverwrite`, with an optional field‑name remap (oldKey → newKey) to survive aggressive renames.

> Built by a Unity game developer, for fellow Unity devs who iterate fast and refuse to lose content.

---

## What’s new

- **Per‑asset context menu (right‑click):** in the Project window, right‑click any `ScriptableObject` to quickly **Backup**, **Restore** or **Diff vs. Backup** just that asset. Ideal for focused edits and sanity checks without scanning whole folders.
- **Diff vs. Backup:** preview the differences between the selected asset and its latest JSON backup **before** restoring. Changes are highlighted by **Added / Removed / Modified** keys, so you can verify exactly what will change.

> The classic project‑wide menu commands are still available and unchanged.

---

## Why

Unity serializes by field name. Rename a `[SerializeField]`, change a type, or move classes around and your `.asset` data can “disappear” from the Inspector. Attributes like `[FormerlySerializedAs]` and `[MovedFrom]` help (use them!), but large refactors and multi‑branch merges still benefit from a belt‑and‑suspenders approach.

SOBackup gives you a simple, inspectable, versionable JSON snapshot per asset that you can safely restore after the dust settles.

---

## What it does

- Backup every `ScriptableObject` in a scope to `{GUID}.json`.
- Restore values into existing assets using `FromJsonOverwrite`.
- Optional key remap (e.g., `"damage" → "baseDamage"`) applied to JSON before restore.
- Works with Inspector‑style serialization: `[SerializeField]`, lists, enums, nested data, and asset references (`{fileID, guid, type}`).
- **Per‑asset actions via context menu:** backup/restore/diff a single asset directly from the Project window.
- **Diff preview:** see Added / Removed / Modified fields compared to the selected backup, so you can review changes prior to restoring.

---

## Quick Start

1) **Install**  
   Drop `SOBackup.cs` into any `.../Editor/` folder (e.g., `Assets/Tools/Editor/`). No package or asmdef required (unless you prefer an editor‑only asmdef).

2) **Backup (project‑wide)**  
   In the Project window, select the folders you want to scan (or select none to scan all of `Assets/`).  
   Menu: **Tools → Contrappasso → Backup ScriptableObjects to JSON…**

3) **Refactor**  
   Rename fields/classes/namespaces as needed. Prefer adding `[FormerlySerializedAs]` / `[MovedFrom]` where feasible.

4) **(Optional) Field‑name remap**  
   Edit the `keyRenames` dictionary inside `SOBackup` to map `old → new` field names before restoring.

5) **Restore (project‑wide)**  
   Menu: **Tools → Contrappasso → Restore ScriptableObjects from JSON…**  
   Pick the backup folder with `{GUID}.json` files. Values are overwritten onto existing assets.

6) **Per‑asset workflow (new)**  
   - Right‑click a `ScriptableObject` in the Project window → **SOBackup** → choose **Backup**, **Restore**, or **Diff vs. Backup**.
   - **Diff vs. Backup** opens a preview of changes (Added / Removed / Modified) so you can validate before applying a restore.

---

## How it works

- **Backup:** Finds `t:ScriptableObject` assets under your folders; writes one JSON per asset, named by GUID to avoid path/rename issues.
- **Restore:** Reads JSON, optionally rewrites keys, resolves the GUID back to an asset, and calls `EditorJsonUtility.FromJsonOverwrite`. Marks assets dirty and saves.
- **Diff:** Loads the current asset state and the corresponding JSON backup, normalizes keys (after optional remap), and shows a minimal set‑difference view so you can spot Added / Removed / Modified fields at a glance.

Because everything is keyed by GUID, moving or renaming assets will not break the mapping.

---

## Best Practices

- Project Settings → Editor: enable **Visible Meta Files** and **Force Text**.
- Prefer private `[SerializeField]` + public properties to reduce future serialized name changes.
- Keep `[FormerlySerializedAs]` and `[MovedFrom]` for a couple of releases after refactors.
- For big migrations: **Backup → Refactor → Diff → Restore →** sanity‑check a few “sentinel” assets/scenes.

---

## Limitations

- Only serializes Unity‑serializable data (public or `[SerializeField]`).
- Overwrites values on existing assets; does not create new assets.
- Incompatible type changes won’t magically deserialize; use migration code (`ISerializationCallbackReceiver`) when semantics change.
- Diff shows structural/value differences in serialized data; it does not execute custom comparison logic or domain‑specific validations.

---

## FAQ

**Do I have to put the script in `Assets/Editor`?**  
Any folder named `Editor` under `Assets/` works (`Assets/Scripts/Editor`, `Assets/Tools/Editor`, etc.). With asmdefs, mark the assembly as Editor‑only. Alternatively, wrap the code with `#if UNITY_EDITOR`.

**Does it handle asset references?**  
Yes. References are preserved in JSON as `{fileID, guid, type}` and re‑bound on restore.

**Why JSON?**  
It’s transparent, diff‑friendly, greppable, and mirrors the Inspector’s serialization via `EditorJsonUtility`.

---

## Changelog

- **[NEW] Per‑asset context menu (right‑click) for Backup / Restore / Diff.**
- **[NEW] Diff vs. Backup to preview Added / Removed / Modified fields before restore.**

---

## License

MIT. Use it, tweak it, ship it.

---

## Credits

Created for the game project **Contrappasso** to move faster without losing ScriptableObject data. If this saved you a headache, consider a ⭐ and share improvements!


---

## Credits
Created for my game project **Contrappasso** to move faster without losing ScriptableObject data. If this saved you a headache, consider a ⭐ and share improvements!
* **GitHub repo**: https://github.com/Ryadel/SOBackup/
* **Docs, demo and examples**: https://www.ryadel.com/en/sobackup-backup-restore-scriptableobjects-json-unity-editor/

