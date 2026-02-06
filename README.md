# Locker

A simple Windows 11 tool that adds "Lock/Unlock Folder" to your right-click context menu. Encrypt folders with a password or just hide them.

## What it does

- **Hide Folder**: Makes a folder invisible (sets hidden + system attributes). Quick and easy, but not secure - anyone who enables "show hidden files" can see it.
- **Lock Folder**: Encrypts all files inside a folder with AES-256. You'll need the password to get them back. The folder gets a `.locker` file that stores the encrypted file list.

## Setup

1. Build or download `Locker.exe`
2. Open a terminal **as Administrator**
3. Run:
   ```
   Locker.exe install
   ```

That's it. Right-click any folder and you'll see the new options.

## To remove

Run as admin:
```
Locker.exe uninstall
```

## Building from source

```
dotnet publish -c Release
```

The exe will be in `bin\Release\net8.0-windows\win-x64\publish\`

## Command line usage

You can also use it directly without the context menu:

```
Locker.exe hide "C:\MyFolder"      # Hide folder
Locker.exe unhide "C:\MyFolder"    # Show it again

Locker.exe lock "C:\MyFolder"      # Encrypt with password
Locker.exe unlock "C:\MyFolder"    # Decrypt with password
```

## How the encryption works

- Uses PBKDF2 with 100,000 iterations to derive a key from your password
- AES-256 in CBC mode encrypts each file
- Original file names are stored in an encrypted manifest
- Files get renamed to random GUIDs while locked

## Notes

- Don't forget your password. There's no recovery.
- The `.locker` file must stay in the folder - it contains the encrypted file list.
- Locking large folders takes time since every file gets encrypted individually.
- This is a personal tool, not enterprise security software. Use it for keeping things private on your own machine.

## License

Do whatever you want with it. Don't clain it as yours though.