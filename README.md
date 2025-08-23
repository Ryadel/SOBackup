# SOBackup — A Safety Net for Unity ScriptableObjects
**Stop losing data on refactors with a GUID-based JSON backup & restore workflow**

SOBackup is an **Editor-only** utility I use to protect **ScriptableObject** data during schema changes. It backs up assets to **pretty-printed JSON** (named by asset **GUID**) and restores values later via `EditorJsonUtility.FromJsonOverwrite`, with an optional **field-name remap** (oldKey → newKey) to survive aggressive renames.

> Built by a Unity game developer, for fellow Unity devs who iterate fast and refuse to lose content.

---

## Why
Unity serializes by **field name**. Rename a `[SerializeField]`, change a type, or move classes around and your `.asset` data can “disappear” from the Inspector. Attributes like `[FormerlySerializedAs]` and `[MovedFrom]` help (use them!), but large refactors and multi-branch merges still benefit from a **belt-and-suspenders** approach.

SOBackup gives you a simple, inspectable, versionable **JSON snapshot** per asset that you can safely restore after the dust settles.

---

## What it does
- **Backup** every ScriptableObject in a scope to `{GUID}.json`.
- **Restore** values into existing assets using `FromJsonOverwrite`.
- Optional **key remap** (e.g., `"damage" → "baseDamage"`) applied to JSON before restore.
- Works with Inspector-style serialization: `[SerializeField]`, lists, enums, nested data, and asset references (`{fileID, guid, type}`).

---

## Quick Start
1. **Install**
   - Drop `SOBackup.cs` into any `.../Editor/` folder (e.g., `Assets/Tools/Editor/`).
   - No package or asmdef required (unless you prefer an editor-only asmdef).

2. **Backup**
   - In the Project window, select the folders you want to scan (or select none to scan all of `Assets/`).
   - Menu: **Tools → Contrappasso → Backup ScriptableObjects to JSON…**
   - Choose an output folder (recommended **outside** `Assets/`).

3. **Refactor**
   - Rename fields/classes/namespaces as needed.
   - Prefer adding `[FormerlySerializedAs]` / `[MovedFrom]` where feasible.

4. **(Optional) Field-name remap**
   - Edit the `keyRenames` dictionary inside `SOBackup` to map old → new field names before restoring.

   ```csharp
   // Example remap (inside SOBackup before restore)
   var keyRenames = new Dictionary<string, string>
   {
       { "damage",      "baseDamage" },
       { "rangeMeters", "range"      },
   };
   ```

5. **Restore**
   - Menu: **Tools → Contrappasso → Restore ScriptableObjects from JSON…**
   - Pick the backup folder with `{GUID}.json` files. Values are overwritten onto existing assets.

---

## How it works
- **Backup:** Finds `t:ScriptableObject` assets under your folders; writes one JSON per asset, named by **GUID** to avoid path/rename issues.
- **Restore:** Reads JSON, optionally rewrites keys, resolves the GUID back to an asset, and calls `EditorJsonUtility.FromJsonOverwrite`. Marks assets dirty and saves.

Because everything is keyed by **GUID**, moving or renaming assets will not break the mapping.

---

## Best Practices
- Project Settings → **Editor**: enable **Visible Meta Files** and **Force Text**.
- Prefer **private `[SerializeField]` + public properties** to reduce future serialized name changes.
- Keep `[FormerlySerializedAs]` and `[MovedFrom]` for a couple of releases after refactors.
- For big migrations: Backup → Refactor → Restore → sanity-check a few “sentinel” assets/scenes.

---

## Limitations
- Only serializes Unity-serializable data (public or `[SerializeField]`).
- **Overwrites** values on existing assets; does **not** create new assets.
- Incompatible **type changes** won’t magically deserialize; use migration code (`ISerializationCallbackReceiver`) when semantics change.

---

## FAQ

**Q: Do I have to put the script in `Assets/Editor`?**  
A: Any folder named `Editor` under `Assets/` works (`Assets/Scripts/Editor`, `Assets/Tools/Editor`, etc.). With asmdefs, mark the assembly as **Editor-only**. Alternatively, wrap the code with `#if UNITY_EDITOR`.

**Q: Does it handle asset references?**  
A: Yes. References are preserved in JSON as `{fileID, guid, type}` and re-bound on restore.

**Q: Why JSON?**  
A: It’s transparent, diff-friendly, greppable, and mirrors the Inspector’s serialization via `EditorJsonUtility`.

---

## License
MIT. Use it, tweak it, ship it.

---

## Credits
Created for my game project **Contrappasso** to move faster without losing ScriptableObject data. If this saved you a headache, consider a ⭐ and share improvements!
* **GitHub repo**: https://github.com/Ryadel/SOBackup/
* **Docs, demo and examples**: https://www.ryadel.com/en/sobackup-backup-restore-scriptableobjects-json-unity-editor/

