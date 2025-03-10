using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace DMotion.Editor
{
    public abstract class PlayableGraphPreview : IDisposable
    {
        private PreviewRenderUtility previewRenderUtility;
        private GameObject gameObject;
        protected Animator animator;
        private SkinnedMeshRenderer skinnedMeshRenderer;
        private Mesh previewMesh;
        private PlayableGraph playableGraph;

        public GameObject GameObject
        {
            get => gameObject;
            set => SetGameObjectPreview(value);
        }

        protected abstract PlayableGraph BuildGraph();
        protected abstract IEnumerable<AnimationClip> Clips { get; }
        public abstract float SampleTime { get; }
        public abstract float NormalizedSampleTime { get; set; }

        private float camDistance;
        private Vector3 camPivot;
        private Vector3 lookAtOffset;
        private Vector2 camEuler;
        private Vector2 lastMousePosition;
        private Boolean isMouseDrag;

        public void Initialize()
        {
            AnimationMode.StartAnimationMode();
            RefreshPreviewObjects();
        }

        private void SetGameObjectPreview(GameObject newValue)
        {
            if (gameObject == newValue)
            {
                return;
            }

            gameObject = newValue;
            if (gameObject != null)
            {
                if (!TryInstantiateSkinnedMesh(gameObject))
                {
                    DestroyPreviewInstance();
                    gameObject = null;
                }
            }
            else
            {
                DestroyPreviewInstance();
            }
        }

        private bool IsValidGameObject(GameObject obj)
        {
            return obj.GetComponentInChildren<Animator>() != null;
        }

        private void DestroyPreviewInstance()
        {
            if (skinnedMeshRenderer != null)
            {
                Object.DestroyImmediate(skinnedMeshRenderer.transform.root.gameObject);
                skinnedMeshRenderer = null;
                Object.DestroyImmediate(previewMesh);
                previewMesh = null;
            }
        }


        private bool TryInstantiateSkinnedMesh(GameObject template)
        {
            if (!IsValidGameObject(template))
            {
                return false;
            }

            DestroyPreviewInstance();

            var instance = Object.Instantiate(template, Vector3.zero, Quaternion.identity);

            AnimatorUtility.DeoptimizeTransformHierarchy(instance);

            instance.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild | HideFlags.HideInHierarchy |
                                 HideFlags.HideInInspector | HideFlags.NotEditable;
            // leaving this here for debug purposes
            // instance.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            animator = instance.GetComponentInChildren<Animator>();
            skinnedMeshRenderer = instance.GetComponentInChildren<SkinnedMeshRenderer>();
            instance.SetActive(false);

            //Make sure mesh is alined to view
            skinnedMeshRenderer.transform.up = Vector3.up;
            skinnedMeshRenderer.transform.forward = Vector3.forward;

            previewMesh = new Mesh();

            if (playableGraph.IsValid())
            {
                playableGraph.Destroy();
            }

            playableGraph = BuildGraph();

            CreatePreviewUtility();

            return true;
        }

        // private void CreatePreviewUtility()
        // {
        //     previewRenderUtility?.Cleanup();
        //     previewRenderUtility = new PreviewRenderUtility();
        //
        //     var light1 = previewRenderUtility.lights[0];
        //     light1.type = LightType.Directional;
        //     light1.color = Color.white;
        //     light1.intensity = 1;
        //
        //     previewRenderUtility.camera.nearClipPlane = 0.3f;
        //     previewRenderUtility.camera.farClipPlane = 3000f;
        //
        //     camPivot = skinnedMeshRenderer.transform.position;
        //     lookAtOffset = Vector3.up * skinnedMeshRenderer.bounds.size.y / 2;
        //     camDistance = 10;
        //     camEuler = new Vector2(45, 30);
        //
        //     isMouseDrag = false;
        //     lastMousePosition = Vector2.zero;
        //     HandleCamera(true);
        // }
        
        private void CreatePreviewUtility()
        {
            previewRenderUtility?.Cleanup();
            previewRenderUtility = new PreviewRenderUtility();

            var bounds = skinnedMeshRenderer.bounds;
            var camPos = new Vector3(0f, bounds.size.y * 3, 10f);
            previewRenderUtility.camera.transform.position = camPos;
            var camRot = Quaternion.LookRotation(bounds.center - camPos);
            previewRenderUtility.camera.transform.rotation = camRot;
            previewRenderUtility.camera.nearClipPlane = 0.3f;
            previewRenderUtility.camera.farClipPlane = 3000f;

            var light1 = previewRenderUtility.lights[0];
            light1.type = LightType.Directional;
            light1.color = Color.white;
            light1.intensity = 1;
            light1.transform.rotation = camRot;
        }

        public void RefreshPreviewObjects()
        {
            if (gameObject != null)
            {
                if (!TryInstantiateSkinnedMesh(gameObject))
                {
                    gameObject = null;
                }
            }
            else
            {
                foreach (var clip in Clips)
                {
                    if (TryFindSkeletonFromClip(clip, out var armatureGo))
                    {
                        if (TryInstantiateSkinnedMesh(armatureGo))
                        {
                            gameObject = armatureGo;
                            break;
                        }
                    }
                }
            }
        }

        public void Dispose()
        {
            AnimationMode.StopAnimationMode();
            previewRenderUtility?.Cleanup();
            DestroyPreviewInstance();
        }

        private bool TryFindSkeletonFromClip(AnimationClip Clip, out GameObject armatureGo)
        {
            var path = AssetDatabase.GetAssetPath(Clip);
            var owner = AssetDatabase.LoadMainAssetAtPath(path);
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(owner)) as ModelImporter;

            var avatar = importer != null ? importer.sourceAvatar : null;
            if (avatar == null && owner is GameObject ownerGo && ownerGo.TryGetComponent<Animator>(out var anim))
            {
                avatar = anim.avatar;
            }
            
            if (avatar != null)
            {
                var avatarPath = AssetDatabase.GetAssetPath(avatar);
                var avatarOwner = AssetDatabase.LoadMainAssetAtPath(avatarPath);
                if (avatarOwner is GameObject go)
                {
                    armatureGo = go;
                    return IsValidGameObject(go);
                }
            }

            armatureGo = null;
            return false;
        }

        public void DrawPreview(Rect r, GUIStyle background)
        {
            if (skinnedMeshRenderer != null && playableGraph.IsValid())
            {
                Assert.IsTrue(AnimationMode.InAnimationMode(), "AnimationMode disabled, make sure to call Initialize");
                AnimationMode.BeginSampling();
                AnimationMode.SamplePlayableGraph(playableGraph, 0, SampleTime);
                AnimationMode.EndSampling();
                {
                    skinnedMeshRenderer.BakeMesh(previewMesh);
                    previewRenderUtility.BeginPreview(r, background);

                    for (var i = 0; i < previewMesh.subMeshCount; i++)
                    {
                        previewRenderUtility.DrawMesh(previewMesh, Matrix4x4.identity,
                            skinnedMeshRenderer.sharedMaterials[i], i);
                    }

                    // HandleCamera();
                    previewRenderUtility.camera.Render();
                    var resultRender = previewRenderUtility.EndPreview();
                    GUI.DrawTexture(r, resultRender, ScaleMode.StretchToFill, false);
                }
            }
        }

        private void HandleCamera(bool force = false)
        {
            if (Event.current == null)
            {
                return;
            }

            // must set hotControl or MouseUp event will not be detected outside window
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            if (Event.current.GetTypeForControl(controlId) == EventType.MouseDown)
                GUIUtility.hotControl = controlId;

            var isMouseDown = Event.current.type == EventType.MouseDown;
            var isMouseUp = Event.current.type == EventType.MouseUp;

            // track mouse drag on our own because out of window event types are "Layout" or "repaint"
            if (Event.current.type == EventType.MouseDrag)
                isMouseDrag = true;

            if (force || isMouseDrag)
            {
                Vector2 delta = Event.current.mousePosition - lastMousePosition;

                // ignore large deltas from SetWantsMouseJumping (Screen.currentResolution minus 20)
                if (Mathf.Abs(delta.x) > Screen.width) delta.x = 0;
                if (Mathf.Abs(delta.y) > Screen.height) delta.y = 0;

                camEuler += delta;

                camEuler.y = Mathf.Clamp(camEuler.y, -179, -1); // -1 to avoid perpendicular angles

                previewRenderUtility.camera.transform.position =
                    Quaternion.Euler(-camEuler.y, camEuler.x, 0) * (Vector3.up * camDistance - camPivot) + camPivot;
                previewRenderUtility.camera.transform.LookAt(camPivot + lookAtOffset);

                // matcap feel
                previewRenderUtility.lights[0].transform.rotation = previewRenderUtility.camera.transform.rotation;

                // needed for repaint
                GUI.changed = true;
            }

            // Undocumented feature that wraps the cursor around the screen edges by Screen.currentResolution minus 20
            if (isMouseDown) EditorGUIUtility.SetWantsMouseJumping(1);
            if (isMouseUp)
            {
                EditorGUIUtility.SetWantsMouseJumping(0);
                isMouseDrag = false;
            }

            // store lastMousePosition starting with mouseDown to prevent wrong initial delta
            if (isMouseDown || isMouseDrag)
            {
                lastMousePosition = Event.current.mousePosition;
            }
        }
    }
}