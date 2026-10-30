// Assets/nur_ieren/ProjectFolderHider.cs
// Editor外のフォルダでもビルドが壊れないよう #if UNITY_EDITOR で囲んでいる。
#if UNITY_EDITOR
// Unity 2022.3.22f1 想定。Projectウィンドウのツリー表示から、指定フォルダを「見た目だけ」消す。
// 実ファイル・.meta・GUIDには一切触らない。設定は UserSettings/ に保存（Git管理外）。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

[InitializeOnLoad]
public static class ProjectFolderHider
{
    const string SavePath = "UserSettings/HiddenProjectFolders.txt";
    const BindingFlags All = BindingFlags.Instance | BindingFlags.Static |
                             BindingFlags.Public | BindingFlags.NonPublic;

    static readonly HashSet<string> hiddenGuids = new HashSet<string>();
    static List<string> hiddenPaths = new List<string>();
    static readonly Dictionary<int, string> pathCache = new Dictionary<int, string>();

    static readonly Type BrowserType =
        typeof(Editor).Assembly.GetType("UnityEditor.ProjectBrowser");

    // ツリーごと（ブラウザ+フィールド名）の直前のスクロール位置と行数
    static readonly Dictionary<string, Vector2> lastScroll = new Dictionary<string, Vector2>();
    static readonly Dictionary<string, int> lastCount = new Dictionary<string, int>();

    static double lastRun;
    static bool warned;

    // ---------- 言語 ----------
    const string LangKey = "nur_ieren.ProjectFolderHider.Language"; // "ja" or "en"（Unity全体で共通の設定）

    public static bool IsJapanese => EditorPrefs.GetString(LangKey, "ja") == "ja";
    public static string T(string ja, string en) => IsJapanese ? ja : en;
    public static void SetLanguage(bool japanese) => EditorPrefs.SetString(LangKey, japanese ? "ja" : "en");

    static ProjectFolderHider()
    {
        Load();
        RebuildPaths();
        EditorApplication.update += OnUpdate;
        EditorApplication.projectChanged += () => { pathCache.Clear(); RebuildPaths(); };
    }

    // ---------- メニュー ----------

    [MenuItem("Assets/Toggle Folder in Project View", true)]
    static bool ToggleValidate() =>
        Selection.assetGUIDs.Any(g => AssetDatabase.IsValidFolder(AssetDatabase.GUIDToAssetPath(g)));

    [MenuItem("Assets/Toggle Folder in Project View", false, 1900)]
    static void Toggle()
    {
        foreach (var guid in Selection.assetGUIDs)
        {
            if (!AssetDatabase.IsValidFolder(AssetDatabase.GUIDToAssetPath(guid))) continue;
            if (!hiddenGuids.Add(guid)) hiddenGuids.Remove(guid);
        }
        Changed();
    }

    public static IReadOnlyCollection<string> HiddenGuids => hiddenGuids;

    public static void Show(string guid) { hiddenGuids.Remove(guid); Changed(); }
    public static void ShowAll() { hiddenGuids.Clear(); Changed(); }

    static void Changed()
    {
        Save();
        RebuildPaths();
        // 隠す→表示に戻す時はツリーを作り直す必要があるので、全ブラウザを再読込
        foreach (var b in GetBrowsers())
        {
            foreach (var name in new[] { "m_AssetTree", "m_FolderTree" })
            {
                var tree = FindField(b.GetType(), name)?.GetValue(b);
                tree?.GetType().GetMethod("ReloadData", All, null, Type.EmptyTypes, null)?.Invoke(tree, null);
            }
            b.Repaint();
        }
        EditorApplication.RepaintProjectWindow();
    }

    // ---------- 永続化 ----------

    static void Load()
    {
        hiddenGuids.Clear();
        if (!File.Exists(SavePath)) return;
        foreach (var line in File.ReadAllLines(SavePath))
            if (!string.IsNullOrWhiteSpace(line)) hiddenGuids.Add(line.Trim());
    }

    static void Save()
    {
        Directory.CreateDirectory("UserSettings");
        File.WriteAllLines(SavePath, hiddenGuids);
    }

    static void RebuildPaths()
    {
        // GUID保存なので、フォルダをリネーム・移動しても追従する
        hiddenPaths = hiddenGuids
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();
    }

    // ---------- リフレクションで行を間引く ----------

    static IEnumerable<EditorWindow> GetBrowsers()
    {
        if (BrowserType == null) yield break;
        foreach (var o in Resources.FindObjectsOfTypeAll(BrowserType))
            if (o is EditorWindow w) yield return w;
    }

    static FieldInfo FindField(Type t, string name)
    {
        while (t != null)
        {
            var f = t.GetField(name, All | BindingFlags.DeclaredOnly);
            if (f != null) return f;
            t = t.BaseType;
        }
        return null;
    }

    static bool IsHidden(int instanceId)
    {
        if (!pathCache.TryGetValue(instanceId, out var path))
            pathCache[instanceId] = path = AssetDatabase.GetAssetPath(instanceId);
        if (string.IsNullOrEmpty(path)) return false;
        foreach (var h in hiddenPaths)
            if (path == h || path.StartsWith(h + "/", StringComparison.Ordinal)) return true;
        return false;
    }

    static void OnUpdate()
    {
        if (hiddenPaths.Count == 0) return;
        if (EditorApplication.timeSinceStartup - lastRun < 0.05) return;
        lastRun = EditorApplication.timeSinceStartup;

        foreach (var browser in GetBrowsers())
        {
            bool removed = false;
            foreach (var name in new[] { "m_AssetTree", "m_FolderTree" })
                removed |= FilterTree(browser, name);
            if (removed) browser.Repaint();
        }
    }

    static bool FilterTree(EditorWindow browser, string fieldName)
    {
        try
        {
            var tree = FindField(browser.GetType(), fieldName)?.GetValue(browser);
            if (tree == null) return false;

            // TreeViewController.data -> ITreeViewDataSource
            var ds = tree.GetType().GetProperty("data", All)?.GetValue(tree);
            if (ds == null) return false;

            var rows = FindField(ds.GetType(), "m_Rows")?.GetValue(ds) as IList<TreeViewItem>;
            if (rows == null)
            {
                Warn(T("m_Rows が見つかりません。内部構造が想定と違います。", "m_Rows not found. The internal structure differs from what is expected."));
                return false;
            }

            bool removed = false;
            for (int i = rows.Count - 1; i >= 0; i--)
            {
                if (IsHidden(rows[i].id)) { rows.RemoveAt(i); removed = true; }
            }

            // 行の再構築でスクロールが先頭に戻されるのを防ぐ。
            // 直前まで下にいたのに、行を間引いたタイミングで先頭に飛んでいたら元に戻す。
            // 開いたフォルダに隠し対象が含まれない場合は removed が false になるので、
            // 「行数が変わった（=展開/折りたたみで再構築された）」ことも条件に含める。
            var state = tree.GetType().GetProperty("state", All)?.GetValue(tree) as TreeViewState;
            if (state != null)
            {
                string key = browser.GetInstanceID() + fieldName;
                int count = rows.Count;
                bool rowsChanged = removed ||
                                   (lastCount.TryGetValue(key, out var prevCount) && prevCount != count);
                lastCount[key] = count;

                if (rowsChanged && state.scrollPos.y < 1f &&
                    lastScroll.TryGetValue(key, out var prev) && prev.y > 1f)
                    state.scrollPos = prev;
                else
                    lastScroll[key] = state.scrollPos;
            }
            return removed;
        }
        catch (Exception e)
        {
            Warn(T("リフレクションに失敗: ", "Reflection failed: ") + e.Message);
            return false;
        }
    }

    static void Warn(string msg)
    {
        if (warned) return;
        warned = true;
        Debug.LogWarning("[ProjectFolderHider] " + msg);
    }
}

// 隠したフォルダの一覧・復帰用ウィンドウ（ツリーから消えるので復帰口が必要）
public class HiddenFoldersWindow : EditorWindow
{
    Vector2 scroll;

    [MenuItem("nur_ieren/ProjectFolderHider/Hidden Folders", false, 0)]
    static void Open() => GetWindow<HiddenFoldersWindow>(ProjectFolderHider.T("隠しフォルダ", "Hidden Folders"));

    void OnGUI()
    {
        titleContent = new GUIContent(ProjectFolderHider.T("隠しフォルダ", "Hidden Folders"));

        // 言語切り替え
        int current = ProjectFolderHider.IsJapanese ? 0 : 1;
        int selected = GUILayout.Toolbar(current, new[] { "日本語", "English" }, EditorStyles.toolbarButton);
        if (selected != current) ProjectFolderHider.SetLanguage(selected == 0);
        EditorGUILayout.Space(4);

        if (GUILayout.Button(ProjectFolderHider.T("すべて表示", "Show All"))) ProjectFolderHider.ShowAll();

        var guids = ProjectFolderHider.HiddenGuids.ToList();
        if (guids.Count == 0)
            EditorGUILayout.HelpBox(ProjectFolderHider.T("隠しているフォルダはありません。", "No hidden folders."), MessageType.Info);

        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(string.IsNullOrEmpty(path)
                    ? ProjectFolderHider.T("(見つかりません) ", "(missing) ") + guid
                    : path);
                if (GUILayout.Button(ProjectFolderHider.T("表示", "Show"), GUILayout.Width(50))) ProjectFolderHider.Show(guid);
            }
        }
        EditorGUILayout.EndScrollView();
    }

    void OnInspectorUpdate() => Repaint();
}
#endif
