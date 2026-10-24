using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using System.Text;
using Tempo.App.Interop;
using static Tempo.App.Interop.NativeMethods;

namespace Tempo.App.Shell;

internal sealed record AppJumpAction(string Name, string Target);
internal sealed record AppJumpGroup(string Name, IReadOnlyList<AppJumpAction> Actions, bool IsTasks = false);

internal static class AppJumpList
{
    public static string? ResolveAppId(string? launcher)
    {
        if (string.IsNullOrWhiteSpace(launcher)) return null;
        object? resolver = null;
        IJumpShellItem? item = null;
        try
        {
            var iid = typeof(IJumpShellItem).GUID;
            if (CreateShellItem(launcher, 0, ref iid, out item) < 0) return null;
            resolver = Activator.CreateInstance(Type.GetTypeFromCLSID(new("660B90C8-73A9-4B58-8CAE-355B7F55341B"), true)!);
            if (resolver is not IApplicationResolver application || application.GetAppIDForShortcut(item, out var id) < 0)
                return null;
            try { return Marshal.PtrToStringUni(id); }
            finally { Marshal.FreeCoTaskMem(id); }
        }
        catch (COMException) { return null; }
        finally
        {
            if (item is not null) Marshal.ReleaseComObject(item);
            if (resolver is not null) Marshal.ReleaseComObject(resolver);
        }
    }

    public static IReadOnlyList<AppJumpGroup> Read(string? appId)
    {
        if (string.IsNullOrWhiteSpace(appId)) return [];
        var groups = new List<AppJumpGroup>();
        object? custom = null;
        object? automatic = null;
        try
        {
            custom = Activator.CreateInstance(Type.GetTypeFromCLSID(new("77F10CF0-3DB5-4966-B520-B7C54FD35ED6"), true)!);
            automatic = Activator.CreateInstance(Type.GetTypeFromCLSID(new("F0AE1542-F497-484B-A175-A20DB09144BA"), true)!);
            var auto = automatic as IAutomaticList;
            if (auto is not null && auto.Initialize(appId, null, null) < 0) auto = null;
            AddAutomatic("Pinned", 0);
            var list = custom as IDestinationList;
            uint count = 0;
            if (list is not null && list.SetApplicationID(appId) >= 0)
                list.GetCategoryCount(out count);
            for (uint index = 0; index < Math.Min(count, 32); index++)
            {
                if (list!.GetCategory(index, 1, out var category) < 0) continue;
                if (category.Type == 1)
                {
                    if (category.Name == 1) AddAutomatic("Frequent", 2);
                    else if (category.Name == 2) AddAutomatic("Recent", 1);
                    continue;
                }
                if (category.Type is not (0 or 2)) continue;
                var name = category.Type == 2 ? "Tasks" : Marshal.PtrToStringUni(category.Name) ?? "Recent";
                if (category.Type == 0) Marshal.FreeCoTaskMem(category.Name);
                var iid = typeof(IJumpObjectArray).GUID;
                if (list.EnumerateCategoryDestinations(index, ref iid, out var items) >= 0)
                    AddItems(name, items, category.Type == 2);
            }
            if (count == 0) AddAutomatic("Recent", 1);

            void AddAutomatic(string name, int type)
            {
                var iid = typeof(IJumpObjectArray).GUID;
                if (auto is not null && auto.GetList(type, 12, 1, ref iid, out var items) >= 0)
                    AddItems(name, items, false);
            }

            void AddItems(string name, IJumpObjectArray items, bool tasks)
            {
                try
                {
                    var actions = new List<AppJumpAction>();
                    if (items.GetCount(out var length) < 0) return;
                    for (uint i = 0; i < Math.Min(length, 32); i++)
                    {
                        var iid = new Guid("00000000-0000-0000-C000-000000000046");
                        if (items.GetAt(i, ref iid, out var item) < 0) continue;
                        try
                        {
                            var action = ReadItem(item, appId, name, i);
                            if (action is not null) actions.Add(action);
                        }
                        finally { Marshal.ReleaseComObject(item); }
                    }
                    if (actions.Count > 0) groups.Add(new(name, actions, tasks));
                }
                finally { Marshal.ReleaseComObject(items); }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or IOException or UnauthorizedAccessException)
        {
            Log.Error("Could not read app Jump List", ex);
        }
        finally
        {
            if (custom is not null) Marshal.ReleaseComObject(custom);
            if (automatic is not null) Marshal.ReleaseComObject(automatic);
        }
        return groups;
    }

    private static AppJumpAction? ReadItem(object item, string appId, string category, uint index)
    {
        if (item is IShellLinkW link)
        {
            string? title = null;
            if (item is IPropertyStore store)
            {
                var key = new PROPERTYKEY(new("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), 2);
                if (store.GetValue(ref key, out var value) >= 0)
                {
                    try { title = value.GetString(); }
                    finally { PropVariantClear(ref value); }
                }
            }
            if (string.IsNullOrWhiteSpace(title))
            {
                var description = new StringBuilder(1024);
                link.GetDescription(description, description.Capacity);
                title = description.ToString();
            }
            if (string.IsNullOrWhiteSpace(title)) return null;
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Tempo", "JumpLists", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(appId))));
            Directory.CreateDirectory(directory);
            var filename = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(category))) + $"-{index}.lnk";
            var target = Path.Combine(directory, filename);
            ((IPersistFile)item).Save(target, false);
            return new(title, target);
        }
        if (item is IJumpShellItem shell)
        {
            if (shell.GetDisplayName(0, out var name) < 0) return null;
            try
            {
                if (shell.GetDisplayName(0x80028000, out var path) < 0) return null;
                try { return new(Marshal.PtrToStringUni(name)!, Marshal.PtrToStringUni(path)!); }
                finally { Marshal.FreeCoTaskMem(path); }
            }
            finally { Marshal.FreeCoTaskMem(name); }
        }
        return null;
    }

    // Windows shell's destination-reader interfaces are separate from the public writer API.
    [DllImport("shell32.dll", EntryPoint = "SHCreateItemFromParsingName", CharSet = CharSet.Unicode)]
    private static extern int CreateShellItem(string path, nint context, ref Guid iid, out IJumpShellItem item);

    [ComImport, Guid("DE25675A-72DE-44B4-9373-05170450C140"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationResolver
    {
        [PreserveSig] int GetAppIDForShortcut(IJumpShellItem item, out nint appId);
    }

    [ComImport, Guid("507101CD-F6AD-46C8-8E20-EEB9E6BAC47F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDestinationList
    {
        [PreserveSig] int SetMinItems();
        [PreserveSig] int SetApplicationID([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int GetSlotCount();
        [PreserveSig] int GetCategoryCount(out uint count);
        [PreserveSig] int GetCategory(uint index, int flags, out DestinationCategory category);
        [PreserveSig] int DeleteCategory();
        [PreserveSig] int EnumerateCategoryDestinations(uint index, ref Guid iid, out IJumpObjectArray items);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DestinationCategory
    {
        public int Type;
        public nint Name;
        public int Count;
        private int pad0, pad1, pad2, pad3, pad4, pad5, pad6, pad7, pad8, pad9;
    }

    [ComImport, Guid("E9C5EF8D-FD41-4F72-BA87-EB03BAD5817C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAutomaticList
    {
        [PreserveSig] int Initialize([MarshalAs(UnmanagedType.LPWStr)] string id,
            [MarshalAs(UnmanagedType.LPWStr)] string? link, [MarshalAs(UnmanagedType.LPWStr)] string? unused);
        [PreserveSig] int HasList([MarshalAs(UnmanagedType.Bool)] out bool hasList);
        [PreserveSig] int GetList(int type, uint count, uint flags, ref Guid iid, out IJumpObjectArray items);
    }

    [ComImport, Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IJumpObjectArray
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object item);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IJumpShellItem
    {
        [PreserveSig] int BindToHandler(nint context, ref Guid handler, ref Guid iid, out nint result);
        [PreserveSig] int GetParent(out IJumpShellItem parent);
        [PreserveSig] int GetDisplayName(uint type, out nint name);
    }
}
