using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

/// <summary>
/// Mesh combiner with simple GUI to combine mesh with the same material.
/// </summary>
public class CombineMesh : EditorWindow
{
    /// <summary>
    /// If true, keep original mesh before combine.
    /// </summary>
    private bool _keepOrignal = true;

    /// <summary>
    /// If true, ignore combining mesh hidden in the hierachy.
    /// </summary>
    private bool _ignoreHidden = true;

    private PivotMode _pivotMode = PivotMode.Origin;

    /// <summary>
    /// Pivot position of the combined meshes.
    /// </summary>
    private Vector3 _pivotPosition = Vector3.zero;

    /// <summary>
    /// Constant String definitions
    /// </summary>
    private const string DEFAULT_MATERIAL = "Default-Material.mat";
    private const string COMBINED_SUFFIX = "_combinedMesh";
    private const string COMBINED_ROOT = "CombinedRoot";

    /// <summary>
    /// Pivot mode.
    /// </summary>
    private enum PivotMode
    {
        //Combined mesh pivot to use origin.
        Origin,
        //Combined mesh pivot to use editor selection pivot.
        SelectionCenter,
    }

    /// <summary>
    /// Init mesh combiner
    /// </summary>
    [MenuItem("Utility/Mesh Combiner Tool")]
    public static void Init()
    {
        GetWindow <CombineMesh>(true, "Combine Mesh", false);
    }

    /// <summary>
    /// Draw combine mesh GUI.
    /// </summary>
    private void OnGUI()
    {
        this.maxSize = new Vector2(220, 200);
        this.minSize = new Vector2(220, 200);
        GUILayout.BeginVertical();
        GUILayout.Label("Select Meshes to Combine", EditorStyles.boldLabel);

        if (GUILayout.Button("Select All Meshes", GUILayout.Width(200)))
        {
            onSelectAllMeshes();
        }
        if (GUILayout.Button("Clear Selection", GUILayout.Width(200)))
        {
            onClearSelection();
        }

        GUILayout.Space(10);
        _pivotMode = (PivotMode)EditorGUILayout.EnumPopup("New Mesh Pivot", _pivotMode);
        _keepOrignal = (GUILayout.Toggle(_keepOrignal, "Keep Orignal"));
        _ignoreHidden = (GUILayout.Toggle(_ignoreHidden, "Ignore hidden Mesh"));

        if (GUILayout.Button("Combine Selected", GUILayout.Width(200)))
        {
            onCombineSelected();
        }
        GUILayout.EndVertical();
    }

    /// <summary>
    /// Responds to on select all meshes in scene.
    /// </summary>
    private void onSelectAllMeshes()
    {
        MeshFilter[] meshInScene = FindObjectsOfType<MeshFilter>();
        if (meshInScene != null && meshInScene.Length > 0)
        {
            GameObject[] gameObjectsToSelect = new GameObject[meshInScene.Length];
            for (int i = 0; i < meshInScene.Length; i++)
            {
                gameObjectsToSelect[i] = meshInScene[i].gameObject;
            }

            Selection.objects = gameObjectsToSelect;
        }
        else
        {
            Debug.LogError("No mesh found in scene.");
        }
    }

    /// <summary>
    /// Repsonds to button click of clearing current selections.
    /// </summary>
    private void onClearSelection()
    {
        Selection.activeGameObject = null;
    }

    /// <summary>
    /// responds to button on combine selected
    /// </summary>
    private void onCombineSelected()
    {
        Transform[] selected = Selection.transforms;
        List<MeshFilter> selectedMeshFilters = new List<MeshFilter>();

        if (selected != null)
        {
            for (int i = 0; i < selected.Length; i++)
            {
                if (selected[i] != null)
                {
                    MeshFilter[] childMeshFilters = selected[i].GetComponentsInChildren<MeshFilter>();
                    foreach (MeshFilter mf in childMeshFilters)
                    {
                        if (mf.sharedMesh != null && !selectedMeshFilters.Contains(mf))
                        {
                            if (mf.gameObject.activeInHierarchy || !_ignoreHidden)
                            {
                                selectedMeshFilters.Add(mf);
                            }
                        }
                    }
                }
            }

            switch (_pivotMode)
            {
                case PivotMode.Origin:
                    _pivotPosition = Vector3.zero;
                    break;
                case PivotMode.SelectionCenter:
                    _pivotPosition = Tools.handlePosition;
                    break;
                default:
                    _pivotPosition = Vector3.zero;
                    break;
            }
        }

        if (selectedMeshFilters.Count > 0)
        {
            //Sort meshes by material
            Dictionary<Material, List<CombineInstance>> sortedMeshes = sortMeshesByMaterials(selectedMeshFilters);

            //Build combined mesh
            if (sortedMeshes.Count > 0)
            {
                GameObject combinedRoot = new GameObject(COMBINED_ROOT);
                combinedRoot.transform.position = _pivotPosition;

                foreach (Material mat in sortedMeshes.Keys)
                {
                    if (mat != null)
                    {
                        Transform parent = null;
                        if (_pivotMode == PivotMode.SelectionCenter)
                        {
                            parent = combinedRoot.transform;
                        }

                        Mesh combinedMesh = combineMesh(sortedMeshes[mat], mat.name, parent);
                        if (combinedMesh != null)
                        {
                            GameObject matRoot = new GameObject(mat.name);
                            matRoot.transform.position = _pivotPosition;
                            matRoot.transform.SetParent(combinedRoot.transform);

                            MeshFilter meshFilter = matRoot.AddComponent<MeshFilter>();
                            meshFilter.sharedMesh = combinedMesh;

                            MeshRenderer meshRenderer = matRoot.AddComponent<MeshRenderer>();
                            meshRenderer.sharedMaterial = mat;
                        }
                        else
                        {
                            Debug.LogError("Error combining mesh for " + mat.name);
                        }
                    }
                }

                Selection.activeObject = combinedRoot;
            }

            //process selected mesh
            for (int i = 0; i < selectedMeshFilters.Count; i++)
            {
                if (selectedMeshFilters[i] != null)
                {
                    if (selectedMeshFilters[i].gameObject.activeInHierarchy || !_ignoreHidden)
                    {
                        if (_keepOrignal)
                        {
                            selectedMeshFilters[i].gameObject.SetActive(false);
                        }
                        else
                        {
                            DestroyImmediate(selectedMeshFilters[i].gameObject);
                        }
                        
                    }
                }
            }
        }
        else
        {
            Debug.LogError("No Mesh found in selection, please reselect.");
        }

    }

    /// <summary>
    /// Generate dictionary of mesh sorted by materials.
    /// </summary>
    /// <param name="meshFilters">Mesh filters to be sorted.</param>
    /// <returns>Sorted mesh dictionary.</returns>
    private Dictionary<Material, List<CombineInstance>> sortMeshesByMaterials(List<MeshFilter> meshFilters)
    {
        Dictionary<Material, List<CombineInstance>> sortedMeshes = new Dictionary<Material, List<CombineInstance>>();
        if (meshFilters != null)
        {
            foreach (MeshFilter mf in meshFilters)
            {
                if (mf != null)
                {
                    MeshRenderer renderer = mf.GetComponent<MeshRenderer>();
                    if (renderer != null)
                    {
                        Material mat = renderer.sharedMaterial;
                        //Use default material if not mat assigned to mesh
                        if (mat == null)
                        {
                            Debug.LogWarning(System.String.Format("{0} does not has a valid material," +
                                " assigning unity defaul material.", mf.gameObject.name));
                            mat = AssetDatabase.GetBuiltinExtraResource<Material>(DEFAULT_MATERIAL);
                        }

                        CombineInstance instance = new CombineInstance();
                        instance.mesh = mf.sharedMesh;
                        instance.subMeshIndex = 0;
                        instance.transform = mf.transform.localToWorldMatrix;


                        if (!sortedMeshes.ContainsKey(mat))
                        {
                            sortedMeshes.Add(mat, new List<CombineInstance>());
                        }

                        if (sortedMeshes.ContainsKey(mat))
                        {
                            if (sortedMeshes[mat] != null)
                            {
                                sortedMeshes[mat].Add(instance);
                            }
                        }
                    }
                }
            }
        }

        return sortedMeshes;
    }

    /// <summary>
    /// Combine meshes into one mesh.
    /// </summary>
    /// <param name="meshCombineInstances">Mesh combine instances</param>
    /// <returns>Combined new mesh</returns>
    /// <param name="name">Optional name given to the combined mesh</param>
    /// <returns>Combined mesh</returns>
    private Mesh combineMesh(List<CombineInstance> meshCombineInstances, string name = null, Transform parent = null)
    {
        Mesh combinedMesh = null;

        if (meshCombineInstances != null && meshCombineInstances.Count > 0)
        {
            if (parent != null)
            {
                Matrix4x4 parentMatrix = parent.worldToLocalMatrix;
                for(int i = 0; i < meshCombineInstances.Count; i++)
                {
                    CombineInstance newCombineInstance = new CombineInstance();
                    newCombineInstance.mesh = meshCombineInstances[i].mesh;
                    newCombineInstance.subMeshIndex = meshCombineInstances[i].subMeshIndex;
                    Matrix4x4 oldMatrix = meshCombineInstances[i].transform;
                    Matrix4x4 newMatrix = parentMatrix * oldMatrix;
                    newCombineInstance.transform = newMatrix;
                    meshCombineInstances[i] = newCombineInstance;
                }
            }

            combinedMesh = new Mesh();
            if (!string.IsNullOrEmpty(name))
            {
                combinedMesh.name = name + COMBINED_SUFFIX;
            }
            combinedMesh.CombineMeshes(meshCombineInstances.ToArray());
        }

        return combinedMesh;
    }
}
