using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

// Locker - Folder encryption via Windows context menu
// Usage: Locker.exe [command] [path] [options]

Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);
Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

if (args.Length == 0)
{
    ShowHelp();
    return;
}

var command = args[0].ToLower();

switch (command)
{
    case "install":
        InstallContextMenu();
        break;
    case "uninstall":
        UninstallContextMenu();
        break;
    case "lock":
        if (args.Length < 2) { ShowError("Path required"); return; }
        LockFolder(args[1]);
        break;
    case "unlock":
        if (args.Length < 2) { ShowError("Path required"); return; }
        UnlockFolder(args[1]);
        break;
    case "hide":
        if (args.Length < 2) { ShowError("Path required"); return; }
        HideFolder(args[1]);
        break;
    case "unhide":
        if (args.Length < 2) { ShowError("Path required"); return; }
        UnhideFolder(args[1]);
        break;
    default:
        ShowError($"Unknown command: {command}");
        ShowHelp();
        break;
}

void ShowHelp()
{
    MessageBox.Show("""
    Locker - Folder Protection Tool for Windows 11
    
    SETUP:
      Locker.exe install        Add context menu entries (run as admin)
      Locker.exe uninstall      Remove context menu entries (run as admin)
    
    MANUAL USAGE:
      Locker.exe hide <path>    Hide folder (basic)
      Locker.exe unhide <path>  Unhide folder
      Locker.exe lock <path>    Encrypt folder with password
      Locker.exe unlock <path>  Decrypt folder with password
    
    After install, right-click any folder to see Lock/Unlock options.
    """, "Locker", MessageBoxButtons.OK, MessageBoxIcon.Information);
}

void ShowError(string message)
{
    MessageBox.Show(message, "Locker - Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
}

void ShowSuccess(string message)
{
    MessageBox.Show(message, "Locker", MessageBoxButtons.OK, MessageBoxIcon.Information);
}

void InstallContextMenu()
{
    try
    {
        var exePath = Process.GetCurrentProcess().MainModule?.FileName 
            ?? Path.Combine(AppContext.BaseDirectory, "Locker.exe");

        // Lock Folder (encrypt)
        using (var key = Registry.ClassesRoot.CreateSubKey(@"Directory\shell\LockerLock"))
        {
            key.SetValue("", "🔒 Lock Folder");
            key.SetValue("Icon", "shell32.dll,47");
            using var cmdKey = key.CreateSubKey("command");
            cmdKey.SetValue("", $"\"{exePath}\" lock \"%1\"");
        }

        // Unlock Folder (decrypt)
        using (var key = Registry.ClassesRoot.CreateSubKey(@"Directory\shell\LockerUnlock"))
        {
            key.SetValue("", "🔓 Unlock Folder");
            key.SetValue("Icon", "shell32.dll,46");
            using var cmdKey = key.CreateSubKey("command");
            cmdKey.SetValue("", $"\"{exePath}\" unlock \"%1\"");
        }

        // Hide Folder
        using (var key = Registry.ClassesRoot.CreateSubKey(@"Directory\shell\LockerHide"))
        {
            key.SetValue("", "👁️ Hide Folder");
            key.SetValue("Icon", "shell32.dll,54");
            using var cmdKey = key.CreateSubKey("command");
            cmdKey.SetValue("", $"\"{exePath}\" hide \"%1\"");
        }

        // Unhide Folder
        using (var key = Registry.ClassesRoot.CreateSubKey(@"Directory\shell\LockerUnhide"))
        {
            key.SetValue("", "👁️ Unhide Folder");
            key.SetValue("Icon", "shell32.dll,44");
            using var cmdKey = key.CreateSubKey("command");
            cmdKey.SetValue("", $"\"{exePath}\" unhide \"%1\"");
        }

        SHChangeNotify(0x8000000, 0x1000, IntPtr.Zero, IntPtr.Zero);

        ShowSuccess($"Context menu installed!\n\nRight-click any folder to see Lock/Unlock options.\n\nExecutable: {exePath}");
    }
    catch (UnauthorizedAccessException)
    {
        ShowError("Run as Administrator to install context menu.");
    }
    catch (Exception ex)
    {
        ShowError(ex.Message);
    }
}

void UninstallContextMenu()
{
    try
    {
        Registry.ClassesRoot.DeleteSubKeyTree(@"Directory\shell\LockerLock", false);
        Registry.ClassesRoot.DeleteSubKeyTree(@"Directory\shell\LockerUnlock", false);
        Registry.ClassesRoot.DeleteSubKeyTree(@"Directory\shell\LockerHide", false);
        Registry.ClassesRoot.DeleteSubKeyTree(@"Directory\shell\LockerUnhide", false);

        SHChangeNotify(0x8000000, 0x1000, IntPtr.Zero, IntPtr.Zero);

        ShowSuccess("Context menu uninstalled.");
    }
    catch (UnauthorizedAccessException)
    {
        ShowError("Run as Administrator to uninstall context menu.");
    }
    catch (Exception ex)
    {
        ShowError(ex.Message);
    }
}

void HideFolder(string path)
{
    try
    {
        if (!Directory.Exists(path))
        {
            ShowError($"Folder not found: {path}");
            return;
        }

        var di = new DirectoryInfo(path);
        di.Attributes |= FileAttributes.Hidden | FileAttributes.System;

        ShowSuccess($"Folder hidden!\n\n{path}\n\nTip: Enable 'Show hidden files' in Explorer to see it again.");
    }
    catch (Exception ex)
    {
        ShowError($"Error hiding folder: {ex.Message}");
    }
}

void UnhideFolder(string path)
{
    try
    {
        if (!Directory.Exists(path))
        {
            ShowError($"Folder not found: {path}");
            return;
        }

        var di = new DirectoryInfo(path);
        di.Attributes &= ~(FileAttributes.Hidden | FileAttributes.System);

        ShowSuccess($"Folder unhidden!\n\n{path}");
    }
    catch (Exception ex)
    {
        ShowError($"Error unhiding folder: {ex.Message}");
    }
}

void LockFolder(string path)
{
    try
    {
        if (!Directory.Exists(path))
        {
            ShowError($"Folder not found: {path}");
            return;
        }

        var lockFile = Path.Combine(path, ".locker");
        if (File.Exists(lockFile))
        {
            ShowError("Folder is already locked. Unlock it first.");
            return;
        }

        var folderName = Path.GetFileName(path);
        
        // Show password dialog
        using var dialog = new LockerDialog($"Lock: {folderName}", "Enter a password to encrypt this folder:", isConfirm: true);
        if (dialog.ShowDialog() != DialogResult.OK)
            return;

        var password = dialog.Password;

        if (string.IsNullOrEmpty(password))
        {
            ShowError("Password cannot be empty.");
            return;
        }

        // Show progress
        using var progress = new ProgressDialog($"Locking {folderName}...");
        progress.Show();
        Application.DoEvents();

        var salt = RandomNumberGenerator.GetBytes(16);
        var key = DeriveKey(password, salt);
        var manifest = new LockManifest { Salt = Convert.ToBase64String(salt), Files = [] };

        var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".locker"))
            .ToList();

        for (int i = 0; i < files.Count; i++)
        {
            var file = files[i];
            var relativePath = Path.GetRelativePath(path, file);
            var encryptedName = $"{Guid.NewGuid():N}.locked";
            var encryptedPath = Path.Combine(path, encryptedName);

            var fileData = File.ReadAllBytes(file);
            var encrypted = Encrypt(fileData, key);
            File.WriteAllBytes(encryptedPath, encrypted);
            File.Delete(file);

            manifest.Files.Add(new LockedFile { OriginalPath = relativePath, EncryptedName = encryptedName });
            
            progress.UpdateStatus($"Encrypting: {relativePath}", (i + 1) * 100 / files.Count);
            Application.DoEvents();
        }

        // Clean empty directories
        foreach (var dir in Directory.GetDirectories(path, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
        {
            if (!Directory.EnumerateFileSystemEntries(dir).Any())
                Directory.Delete(dir);
        }

        // Write manifest - prepend salt (unencrypted) then encrypted manifest
        var manifestJson = JsonSerializer.Serialize(manifest);
        var manifestEncrypted = Encrypt(Encoding.UTF8.GetBytes(manifestJson), key);

        using (var fs = File.Create(lockFile))
        {
            fs.Write(salt);
            fs.Write(manifestEncrypted);
        }

        // DON'T hide the folder - keep it visible so user can unlock it
        // Just rename it to indicate it's locked
        progress.Close();

        ShowSuccess($"Folder locked!\n\n{files.Count} files encrypted.\n\nThe folder remains visible so you can unlock it later.");
    }
    catch (Exception ex)
    {
        ShowError($"Error: {ex.Message}");
    }
}

void UnlockFolder(string path)
{
    try
    {
        if (!Directory.Exists(path))
        {
            ShowError($"Folder not found: {path}");
            return;
        }

        var lockFile = Path.Combine(path, ".locker");
        if (!File.Exists(lockFile))
        {
            ShowError("Folder is not locked (no .locker file found).");
            return;
        }

        var folderName = Path.GetFileName(path);

        // Show password dialog
        using var dialog = new LockerDialog($"Unlock: {folderName}", "Enter password to decrypt this folder:", isConfirm: false);
        if (dialog.ShowDialog() != DialogResult.OK)
            return;

        var password = dialog.Password;

        var encryptedManifest = File.ReadAllBytes(lockFile);

        if (encryptedManifest.Length < 16)
        {
            ShowError("Corrupted lock file.");
            return;
        }

        var salt = encryptedManifest[..16];
        var manifestData = encryptedManifest[16..];
        var key = DeriveKey(password, salt);

        LockManifest manifest;
        try
        {
            var decrypted = Decrypt(manifestData, key);
            manifest = JsonSerializer.Deserialize<LockManifest>(Encoding.UTF8.GetString(decrypted))!;
        }
        catch
        {
            ShowError("Wrong password or corrupted data.");
            return;
        }

        // Show progress
        using var progress = new ProgressDialog($"Unlocking {folderName}...");
        progress.Show();
        Application.DoEvents();

        for (int i = 0; i < manifest.Files.Count; i++)
        {
            var file = manifest.Files[i];
            var encryptedPath = Path.Combine(path, file.EncryptedName);
            var originalPath = Path.Combine(path, file.OriginalPath);

            if (!File.Exists(encryptedPath))
            {
                continue;
            }

            var encryptedData = File.ReadAllBytes(encryptedPath);
            var decrypted = Decrypt(encryptedData, key);

            Directory.CreateDirectory(Path.GetDirectoryName(originalPath)!);
            File.WriteAllBytes(originalPath, decrypted);
            File.Delete(encryptedPath);

            progress.UpdateStatus($"Restoring: {file.OriginalPath}", (i + 1) * 100 / manifest.Files.Count);
            Application.DoEvents();
        }

        File.Delete(lockFile);
        new DirectoryInfo(path).Attributes &= ~FileAttributes.Hidden;

        progress.Close();
        ShowSuccess($"Folder unlocked!\n\n{manifest.Files.Count} files restored.");
    }
    catch (Exception ex)
    {
        ShowError($"Error: {ex.Message}");
    }
}

byte[] DeriveKey(string password, byte[] salt)
{
    using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 100000, HashAlgorithmName.SHA256);
    return pbkdf2.GetBytes(32);
}

byte[] Encrypt(byte[] data, byte[] key)
{
    using var aes = Aes.Create();
    aes.Key = key;
    aes.GenerateIV();

    using var ms = new MemoryStream();
    ms.Write(aes.IV, 0, aes.IV.Length);

    using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
    {
        cs.Write(data, 0, data.Length);
    }

    return ms.ToArray();
}

byte[] Decrypt(byte[] data, byte[] key)
{
    using var aes = Aes.Create();
    aes.Key = key;

    var iv = data[..16];
    var ciphertext = data[16..];
    aes.IV = iv;

    using var ms = new MemoryStream();
    using (var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Write))
    {
        cs.Write(ciphertext, 0, ciphertext.Length);
    }

    return ms.ToArray();
}

[DllImport("shell32.dll")]
static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);

// Windows 11 styled password dialog
class LockerDialog : Form
{
    private TextBox passwordBox;
    private TextBox confirmBox;
    private Button okButton;
    private Button cancelButton;
    private CheckBox showPassword;
    private bool isConfirm;

    public string Password => passwordBox.Text;

    public LockerDialog(string title, string message, bool isConfirm)
    {
        this.isConfirm = isConfirm;
        
        Text = title;
        Size = new Size(400, isConfirm ? 260 : 200);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(32, 32, 32);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10);

        var label = new Label
        {
            Text = message,
            Location = new Point(20, 20),
            Size = new Size(340, 25),
            ForeColor = Color.FromArgb(230, 230, 230)
        };
        Controls.Add(label);

        passwordBox = new TextBox
        {
            Location = new Point(20, 50),
            Size = new Size(340, 30),
            UseSystemPasswordChar = true,
            BackColor = Color.FromArgb(45, 45, 45),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 11)
        };
        Controls.Add(passwordBox);

        int yOffset = 85;

        if (isConfirm)
        {
            var confirmLabel = new Label
            {
                Text = "Confirm password:",
                Location = new Point(20, yOffset),
                Size = new Size(340, 25),
                ForeColor = Color.FromArgb(230, 230, 230)
            };
            Controls.Add(confirmLabel);
            yOffset += 25;

            confirmBox = new TextBox
            {
                Location = new Point(20, yOffset),
                Size = new Size(340, 30),
                UseSystemPasswordChar = true,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 11)
            };
            Controls.Add(confirmBox);
            yOffset += 40;
        }

        showPassword = new CheckBox
        {
            Text = "Show password",
            Location = new Point(20, yOffset),
            Size = new Size(150, 25),
            ForeColor = Color.FromArgb(200, 200, 200)
        };
        showPassword.CheckedChanged += (s, e) =>
        {
            passwordBox.UseSystemPasswordChar = !showPassword.Checked;
            if (confirmBox != null)
                confirmBox.UseSystemPasswordChar = !showPassword.Checked;
        };
        Controls.Add(showPassword);

        yOffset += 35;

        okButton = new Button
        {
            Text = isConfirm ? "Lock" : "Unlock",
            Location = new Point(170, yOffset),
            Size = new Size(90, 35),
            BackColor = Color.FromArgb(0, 103, 192),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            DialogResult = DialogResult.OK
        };
        okButton.FlatAppearance.BorderSize = 0;
        okButton.Click += OkButton_Click;
        Controls.Add(okButton);

        cancelButton = new Button
        {
            Text = "Cancel",
            Location = new Point(270, yOffset),
            Size = new Size(90, 35),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            DialogResult = DialogResult.Cancel
        };
        cancelButton.FlatAppearance.BorderSize = 0;
        Controls.Add(cancelButton);

        AcceptButton = okButton;
        CancelButton = cancelButton;
    }

    private void OkButton_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(passwordBox.Text))
        {
            MessageBox.Show("Password cannot be empty.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        if (isConfirm && passwordBox.Text != confirmBox.Text)
        {
            MessageBox.Show("Passwords don't match.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }
    }
}

// Progress dialog
class ProgressDialog : Form
{
    private Label statusLabel;
    private ProgressBar progressBar;

    public ProgressDialog(string title)
    {
        Text = title;
        Size = new Size(400, 130);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false;
        BackColor = Color.FromArgb(32, 32, 32);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10);

        statusLabel = new Label
        {
            Text = "Preparing...",
            Location = new Point(20, 20),
            Size = new Size(340, 25),
            ForeColor = Color.FromArgb(230, 230, 230)
        };
        Controls.Add(statusLabel);

        progressBar = new ProgressBar
        {
            Location = new Point(20, 50),
            Size = new Size(340, 25),
            Style = ProgressBarStyle.Continuous
        };
        Controls.Add(progressBar);
    }

    public void UpdateStatus(string status, int percent)
    {
        statusLabel.Text = status;
        progressBar.Value = Math.Min(100, Math.Max(0, percent));
    }
}

class LockManifest
{
    public string Salt { get; set; } = "";
    public List<LockedFile> Files { get; set; } = [];
}

class LockedFile
{
    public string OriginalPath { get; set; } = "";
    public string EncryptedName { get; set; } = "";
}
