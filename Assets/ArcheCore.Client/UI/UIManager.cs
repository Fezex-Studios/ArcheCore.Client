using System.Collections.Generic;
using System.IO;
using ArcheCore.Client.Networking;
using MoonSharp.Interpreter;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArcheCore.Client.UI
{
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        [Header("Root")]
        [SerializeField] private Transform rootCanvas;

        [Header("Prefabs")]
        [SerializeField] private GameObject panelPrefab;
        [SerializeField] private GameObject textPrefab;
        [SerializeField] private GameObject buttonPrefab;

        private readonly Dictionary<string, RectTransform> elements = new();
        private readonly Dictionary<string, TMP_Text> texts = new();
        private readonly Dictionary<string, Image> images = new();
        private readonly Dictionary<string, Button> buttons = new();

        private Script luaScript;

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            InitLua();
        }

        private void InitLua()
        {
            Script.DefaultOptions.ScriptLoader = new MoonSharp.Interpreter.Loaders.FileSystemScriptLoader();

            UserData.UnregisterType(typeof(UIManager));
            UserData.UnregisterType(typeof(LuaNetworkBinding));

            UserData.RegisterType<UIManager>();
            UserData.RegisterType<LuaNetworkBinding>();

            luaScript = new Script();
            luaScript.Globals["UI"] = this;
            luaScript.Globals["Net"] = new LuaNetworkBinding();

            LoadAllScripts();
        }

        // ---------------- Factory API (Lua-callable) ----------------

        public void CreatePanel(string id, float x, float y, float width, float height, string parentId = null)
        {
            if (elements.ContainsKey(id))
            {
                Debug.LogWarning($"[UIManager] Element id '{id}' already exists, overwriting.");
                DestroyElement(id);
            }

            Transform parent = ResolveParent(parentId);
            GameObject go = Instantiate(panelPrefab, parent);
            go.name = id;

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(width, height);

            elements[id] = rt;
            images[id] = go.GetComponent<Image>();
        }

        public void CreateText(string id, string content, float x, float y, float width, float height, string parentId = null)
        {
            Transform parent = ResolveParent(parentId);
            GameObject go = Instantiate(textPrefab, parent);
            go.name = id;

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(width, height);

            TMP_Text text = go.GetComponent<TMP_Text>();
            text.text = content;

            elements[id] = rt;
            texts[id] = text;
        }

        public void CreateButton(string id, string label, float x, float y, float width, float height, string callbackFnName, string parentId = null)
        {
            Transform parent = ResolveParent(parentId);
            GameObject go = Instantiate(buttonPrefab, parent);
            go.name = id;

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(width, height);

            Button btn = go.GetComponent<Button>();
            TMP_Text label_ = go.GetComponentInChildren<TMP_Text>();
            if (label_ != null) label_.text = label;

            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() =>
            {
                Debug.Log($"[UIManager] Button clicked: {id} -> calling {callbackFnName}");
                CallLuaFunction(callbackFnName, id);
            });

            elements[id] = rt;
            buttons[id] = btn;
            images[id] = go.GetComponent<Image>();
        }

        public void DestroyElement(string id)
        {
            if (elements.TryGetValue(id, out var rt))
            {
                Destroy(rt.gameObject);
                elements.Remove(id);
                texts.Remove(id);
                images.Remove(id);
                buttons.Remove(id);
            }
        }

        private Transform ResolveParent(string parentId)
        {
            if (string.IsNullOrEmpty(parentId)) return rootCanvas;
            return elements.TryGetValue(parentId, out var rt) ? rt : rootCanvas;
        }

        // ---------------- Mutators (Lua-callable) ----------------

        public void SetText(string id, string text)
        {
            if (texts.TryGetValue(id, out var t)) t.text = text;
        }

        public void SetColor(string id, float r, float g, float b, float a)
        {
            if (images.TryGetValue(id, out var img)) img.color = new Color(r, g, b, a);
            if (texts.TryGetValue(id, out var txt)) txt.color = new Color(r, g, b, a);
        }

        public void SetPosition(string id, float x, float y)
        {
            if (elements.TryGetValue(id, out var rt)) rt.anchoredPosition = new Vector2(x, y);
        }

        public void Show(string id)
        {
            if (elements.TryGetValue(id, out var rt)) rt.gameObject.SetActive(true);
        }

        public void Hide(string id)
        {
            if (elements.TryGetValue(id, out var rt)) rt.gameObject.SetActive(false);
        }

        // ---------------- Lua dispatch ----------------

        public void CallLuaFunction(string fnName, params object[] args)
        {
            Debug.Log($"[UIManager] CallLuaFunction: {fnName}");

            DynValue fn = luaScript.Globals.Get(fnName);
            if (fn.Type != DataType.Function)
            {
                Debug.LogWarning($"[UIManager] Lua function '{fnName}' not found.");
                return;
            }

            try
            {
                luaScript.Call(fn, args);
            }
            catch (ScriptRuntimeException ex)
            {
                Debug.LogError($"[UIManager] Lua error in '{fnName}': {ex.DecoratedMessage}");
            }
        }

        // ---------------- Script loading ----------------

        private void LoadAllScripts()
        {
            string folder = Path.Combine(Application.streamingAssetsPath, "LuaUI");

            if (!Directory.Exists(folder))
            {
                Debug.LogWarning($"[UIManager] Lua UI folder not found: {folder}");
                return;
            }

            foreach (var file in Directory.GetFiles(folder, "*.lua"))
            {
                try
                {
                    string code = File.ReadAllText(file);
                    luaScript.DoString(code, null, Path.GetFileName(file));
                    Debug.Log($"[UIManager] Loaded Lua script: {Path.GetFileName(file)}");
                }
                catch (ScriptRuntimeException ex)
                {
                    Debug.LogError($"[UIManager] Lua runtime error in {file}: {ex.DecoratedMessage}");
                }
                catch (SyntaxErrorException ex)
                {
                    Debug.LogError($"[UIManager] Lua syntax error in {file}: {ex.DecoratedMessage}");
                }
            }
        }
    }
}