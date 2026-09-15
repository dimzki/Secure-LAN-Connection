using UnityEditor;
using UnityEngine;
using SecureLanConnection;
using System.IO;

namespace SecureLanConnection.Editor
{
    [InitializeOnLoad]
    public class SecureLanSetupWindow : EditorWindow
    {
        private static SecureLanSettings _settings;
        private static bool _initialized;

        static SecureLanSetupWindow()
        {
            EditorApplication.update += RunOnce;
        }

        static void RunOnce()
        {
            EditorApplication.update -= RunOnce;
            if (_initialized) return;
            _initialized = true;

            // Check if settings exist
            LoadOrCreateSettings();

            // Using EditorPrefs to ensure it only shows once per project.
            string prefKey = "SecureLanConnection_SetupShown_" + Application.dataPath.GetHashCode();
            if (!EditorPrefs.GetBool(prefKey, false))
            {
                ShowWindow();
                EditorPrefs.SetBool(prefKey, true);
            }
        }

        [MenuItem("Window/Secure LAN Connection/Setup")]
        public static void ShowWindow()
        {
            var window = GetWindow<SecureLanSetupWindow>("Secure LAN Setup");
            window.minSize = new Vector2(400, 300);
            window.Show();
        }

        private static void LoadOrCreateSettings()
        {
            _settings = Resources.Load<SecureLanSettings>("SecureLanSettings");
            if (_settings == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                {
                    AssetDatabase.CreateFolder("Assets", "Resources");
                }
                
                _settings = CreateInstance<SecureLanSettings>();
                AssetDatabase.CreateAsset(_settings, "Assets/Resources/SecureLanSettings.asset");
                AssetDatabase.SaveAssets();
            }
        }

        private void OnEnable()
        {
            LoadOrCreateSettings();
        }

        private void OnGUI()
        {
            if (_settings == null)
            {
                LoadOrCreateSettings();
                if (_settings == null)
                {
                    EditorGUILayout.HelpBox("Failed to load or create SecureLanSettings.", MessageType.Error);
                    return;
                }
            }

            GUILayout.Space(20);
            
            var titleStyle = new GUIStyle(EditorStyles.largeLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 18
            };
            GUILayout.Label("Secure LAN Connection Setup", titleStyle);
            
            GUILayout.Space(10);
            EditorGUILayout.HelpBox("Welcome! Please configure your LAN Room settings below. These settings apply to all PCs that want to connect to the same room.\n\nNote: Connections are fully automatic and don't require specifying explicit ports.", MessageType.Info);
            
            GUILayout.Space(20);

            EditorGUI.BeginChangeCheck();

            EditorGUILayout.LabelField(new GUIContent("Shared Secret (Room Password)", "Only PCs with this exact secret can connect and communicate with each other."), EditorStyles.boldLabel);
            _settings.sharedSecret = EditorGUILayout.TextField(_settings.sharedSecret);

            GUILayout.Space(10);

            EditorGUILayout.LabelField(new GUIContent("Expected Peer Count", "Set to 0 for open-ended connection. If > 0, the 'OnAllExpectedPeersConnected' event fires once this many peers are connected."), EditorStyles.boldLabel);
            _settings.expectedPeerCount = EditorGUILayout.IntField(_settings.expectedPeerCount);

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(_settings);
                AssetDatabase.SaveAssets();
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Close & Save", GUILayout.Height(30)))
            {
                Close();
            }
            GUILayout.Space(10);
        }
    }
}
