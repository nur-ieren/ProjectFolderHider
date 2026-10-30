# ProjectFolderHider

A Unity Editor tool for hiding and restoring folders in the Project window.

## Concept

ProjectFolderHider was created to reduce clutter in the Unity Project window.

Folders containing tools, packages, or assets that are rarely accessed can make it harder to navigate a large project.

This tool hides selected folders from the Project window without deleting or modifying the underlying files.

## Overview

- **Unity Version:** 2022.3.22f1
- **Hide / Show Menu:** `Assets > Toggle Folder in Project View`
- **Management Window:** `nur_ieren > ProjectFolderHider`

## Features

- **Folder Hiding** — Toggles the visibility of selected folders in the Project window.
- **Persistent Settings** — Saves hidden folder GUIDs to `UserSettings/HiddenProjectFolders.txt`.
- **Rename and Move Support** — Uses asset GUIDs to track folders when their paths change.
- **Restore Individual Folders** — Displays hidden folders in a management window and lets you restore them individually.
- **Restore All Folders** — Provides a `Show All` button to restore every hidden folder.
- **File Safety** — Does not delete or modify the actual asset files, `.meta` files, or GUIDs.

## How to use

### Hide a folder

1. Open the Unity Project window.
2. Select one or more folders.
3. Right-click the selection.
4. Choose `Toggle Folder in Project View`.

Selecting a folder that is already hidden toggles it back to visible.

### Restore hidden folders

1. Open `nur_ieren > ProjectFolderHider`.
2. Find the folder you want to restore.
3. Click `Show` next to that folder.

To restore all hidden folders, click `Show All`.

## Important notes

- This tool changes the visible Project window tree, not the actual contents of your project.
- Hidden folder settings are stored locally in `UserSettings/HiddenProjectFolders.txt`.
- The settings file is separate from the asset files and can be excluded from version control if desired.
- The tool relies on Unity Editor internal APIs through reflection. Changes to Unity's internal Project window implementation may affect compatibility.

## Development

This project was created to make navigating large Unity projects more convenient without removing assets from the project.

## Author

vor_ges

## Licence

This project is licensed under the MIT License. See LICENSE file for details.

[MIT License](https://opensource.org/license/mit)
