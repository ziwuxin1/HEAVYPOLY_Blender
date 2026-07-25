
# HEAVYPOLY for Blender

Custom scripts and pie menus to make Blender faster and easier to use — designed for pen tablet or mouse. Works well for both left- and right-handed artists.

---

## 💾 Download Instructions

### Blender Versions

- **Blender 5.2 LTS and above**  
  ➡️ Click the green **`Code`** button at the top right of this page, then select **`Download ZIP`**.

- **Blender 4.3 – 5.1**  
  Use the last commit before the 5.2 port (tag `v1.1.0-blender4.3`).

- **Blender 4.1 and 4.2**  
  [Download v1.0.0](https://github.com/Renart84/HEAVYPOLY_Blender/releases/tag/v1.0.0)

- **Blender 3.6 and 4.0**  
  [Download older release](https://github.com/HEAVYPOLY/HEAVYPOLY_Blender/releases)

> ⚠️ `master` now targets **Blender 5.2 LTS**. Blender 5.0 removed a large number of Python APIs —
> the `bgl` module, the EEVEE Legacy settings, the legacy Grease Pencil operators, `paint.brush_select`
> and more — so this version will **not** run correctly on Blender 4.x. Use the links above for older Blender.

---
### 🔹 Windows

#### For the **portable version** of Blender:
1. Open the folder where `blender.exe` is located.
2. Create a new folder named:
   ```
   portable
   ```
3. Unzip the `HEAVYPOLY Config` and copy the folders ( Config and Scripts) into the "portable" folder you just created — you should now have:
   ```
   blender-folder/
     └─ portable/
         ├─ config/
         └─ scripts/
   ```
   
#### For the **installed version** of Blender:
1. Unzip the downloaded `HEAVYPOLY Config`.
2. Copy the folders named `config` and `scripts` into:  
   ```
   C:\Users\YOURUSERNAME\AppData\Roaming\Blender Foundation\Blender\5.2\
   ```
   > ⚠️ Replace `5.2` with your actual Blender version.  
   > ⚠️ The `AppData` folder is hidden. Enable **"Show hidden files"** in your File Explorer settings to see it.




### 🔹 macOS

1. In the **Applications** folder, right-click on the Blender app and choose **"Show Package Contents"**.
2. Go to:
   ```
   Contents/Resources
   ```
3. Create a folder named:
   ```
   portable
   ```
4. Unzip the `HEAVYPOLY Config` and copy the folders ( Config and Scripts) into the "portable" folder you just created — you should now have:
   ```
   Blender.app/
     └─ Contents/
         └─ Resources/
             └─ portable/
                 ├─ config/
                 └─ scripts/
   ```

---

## 📝 Notes

- `config/startup.blend` and `config/userpref.blend` are saved from an earlier Blender. Blender
  upgrades them automatically on first load, so they do not need to be re-saved — keeping them in the
  older format is what lets the same files still work on older Blender releases.
- Some EEVEE Legacy controls (Bloom, Soft Shadows, Light Cache baking, the old shadow-buffer
  settings) no longer exist in Blender 5.x and have been removed from the HEAVYPOLY panels. Bloom is
  now done with the **Glare** node in the compositor.
- Sculpt brushes are assets in Blender 5.x, so the sculpt brush buttons now activate brush assets
  from the **Essentials** library instead of using the removed `paint.brush_select` operator.

---

## 🎥 Setup Video (for Blender 3.6 – 4.1)

[Watch the installation tutorial on YouTube](https://www.youtube.com/watch?v=TRESMUenxa8)
