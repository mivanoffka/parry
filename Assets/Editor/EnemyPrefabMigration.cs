using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Parry.Editor
{
    public static class EnemyPrefabMigration
    {
        public const string PrefabPath = "Assets/Resources/Combat/Enemy.prefab";

        [MenuItem("Parry/Unify Enemies Into Prefab")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before converting enemies.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                foreach (var path in new[] { "Assets/Scenes/SampleScene.unity", "Assets/Scenes/0.unity", "Assets/Scenes/AIDuel.unity" })
                {
                    var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    foreach (var root in scene.GetRootGameObjects())
                    foreach (var enemy in root.GetComponentsInChildren<EnemyBrain>(true))
                    {
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                        if (prefab == null)
                        {
                            var copy = UnityEngine.Object.Instantiate(enemy.gameObject);
                            copy.name = "Enemy";
                            copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                            var references = new SerializedObject(copy.GetComponent<EnemyBrain>());
                            references.FindProperty("player").objectReferenceValue = null;
                            references.FindProperty("opponent").objectReferenceValue = null;
                            references.ApplyModifiedPropertiesWithoutUndo();
                            prefab = PrefabUtility.SaveAsPrefabAsset(copy, PrefabPath);
                            UnityEngine.Object.DestroyImmediate(copy);
                        }
                        var before = new SerializedObject(enemy);
                        var player = before.FindProperty("player").objectReferenceValue;
                        var opponent = before.FindProperty("opponent").objectReferenceValue;
                        Vector3 position = enemy.transform.position;
                        Color color = enemy.GetComponentInChildren<SpriteRenderer>().color;
                        if (!PrefabUtility.IsPartOfPrefabInstance(enemy.gameObject))
                            PrefabUtility.ConvertToPrefabInstance(enemy.gameObject, prefab,
                                new ConvertToPrefabInstanceSettings
                                {
                                    objectMatchMode = ObjectMatchMode.ByHierarchy,
                                    recordPropertyOverridesOfMatches = true,
                                    changeRootNameToAssetName = false,
                                    componentsNotMatchedBecomesOverride = true,
                                    gameObjectsNotMatchedBecomesOverride = true
                                }, InteractionMode.AutomatedAction);
                        var after = new SerializedObject(enemy);
                        if (after.FindProperty("player").objectReferenceValue != player
                            || after.FindProperty("opponent").objectReferenceValue != opponent
                            || enemy.transform.position != position
                            || enemy.GetComponentInChildren<SpriteRenderer>().color != color)
                            throw new InvalidOperationException("Enemy settings or scene references changed during conversion.");
                        if (PrefabUtility.GetCorrespondingObjectFromSource(enemy.gameObject) != prefab
                            || enemy.transform.Find("Enemy sprite") == null
                            || enemy.transform.Find("Ground shadow") == null)
                            throw new InvalidOperationException("Enemy prefab structure was not preserved.");
                    }
                    EditorSceneManager.SaveScene(scene);
                    Debug.Log("ENEMY PREFAB MIGRATED: " + path);
                }
                AssetDatabase.SaveAssets();
            }
            finally
            {
                if (Array.Exists(setup, scene => scene.isLoaded && scene.isActive))
                    EditorSceneManager.RestoreSceneManagerSetup(setup);
            }
        }
    }
}
