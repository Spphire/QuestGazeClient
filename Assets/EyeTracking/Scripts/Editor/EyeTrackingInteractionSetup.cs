#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Meta.XR.BuildingBlocks;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Input;
using Oculus.Interaction.Input.Visuals;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EyeTracking.Editor
{
    internal static class EyeTrackingInteractionSetup
    {
        private const string ScenePath = "Assets/EyeTracking/EyeTrackingScene.unity";
        private const string BaseLinkName = "base_link";

        private const string InteractionBlockId = "81f55626-5fad-45e9-a1df-184f330da7ba";
        private const string InteractionHandTrackingBlockId = "0393ca30-f2a9-4865-a40f-f9a68d01c3a9";
        private const string InteractionControllerTrackingBlockId = "f10154e0-16b2-492f-97d0-6639f69e7df6";
        private const string SyntheticHandsBlockId = "6b67162c-2460-4766-a931-980388647573";
        private const string CoreControllerTrackingBlockId = "5817f7c0-f2a5-45f9-a5ca-64264e0166e8";

        [InitializeOnLoadMethod]
        private static void AutoSetup()
        {
            EditorApplication.delayCall -= DelayedAutoSetup;
            EditorApplication.delayCall += DelayedAutoSetup;
        }

        private static void DelayedAutoSetup()
        {
            EditorApplication.delayCall -= DelayedAutoSetup;
            _ = SetupAsync(false);
        }

        [MenuItem("EyeTracking/Setup Meta Interaction For base_link")]
        private static void SetupFromMenu()
        {
            _ = SetupAsync(true);
        }

        private static async Task SetupAsync(bool force)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                return;
            }

            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.path != ScenePath)
            {
                return;
            }

            GameObject baseLink = FindSceneObject(BaseLinkName);
            if (baseLink == null)
            {
                Debug.LogWarning($"EyeTracking Interaction setup skipped: cannot find {BaseLinkName}.");
                return;
            }

            try
            {
                bool changed = false;

                changed |= await InstallBlockIfMissing(InteractionBlockId);
                changed |= await InstallBlockIfMissing(CoreControllerTrackingBlockId);
                changed |= await InstallBlockIfMissing(InteractionHandTrackingBlockId);
                changed |= await InstallBlockIfMissing(InteractionControllerTrackingBlockId);
                changed |= await InstallBlockIfMissing(SyntheticHandsBlockId);
                changed |= ConfigureMetaControllerVisuals();
                changed |= ConfigureMetaControllerInteractors();
                changed |= ConfigureControllerDistanceGrabSelectors();
                changed |= ConfigureLegacyXrTemplateControllerObjects();

                bool hasDistanceGrab = baseLink.GetComponentInChildren<DistanceGrabInteractable>(true) != null;
                if (force || !hasDistanceGrab)
                {
                    ConfigureBaseLink(baseLink);

                    if (!hasDistanceGrab)
                    {
                        CreateDistanceGrabInteractable(baseLink);
                    }

                    ConfigureDistanceGrab(baseLink);
                    changed = true;
                }

                if (changed)
                {
                    EditorSceneManager.MarkSceneDirty(activeScene);
                    EditorSceneManager.SaveScene(activeScene);
                    AssetDatabase.SaveAssets();
                }

                Debug.Log("EyeTracking Interaction setup complete: Meta controller visuals, controller interactors, and base_link Distance Grab are configured.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"EyeTracking Interaction setup failed: {ex}");
            }
        }

        private static void ConfigureBaseLink(GameObject baseLink)
        {
            foreach (MonoBehaviour behaviour in baseLink.GetComponents<MonoBehaviour>())
            {
                if (behaviour != null && behaviour.GetType().Name == "BaseLinkDragController")
                {
                    Undo.DestroyObjectImmediate(behaviour);
                }
            }

            Rigidbody body = baseLink.GetComponent<Rigidbody>();
            if (body == null)
            {
                body = Undo.AddComponent<Rigidbody>(baseLink);
            }

            body.useGravity = false;
            body.isKinematic = true;
            body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        }

        private static void CreateDistanceGrabInteractable(GameObject baseLink)
        {
            Type wizardType = FindType("Oculus.Interaction.Editor.QuickActions.DistanceGrabWizard");
            Type baseWizardType = FindType("Oculus.Interaction.Editor.QuickActions.QuickActionsWizard");
            if (wizardType == null || baseWizardType == null)
            {
                throw new InvalidOperationException("Cannot find Interaction SDK DistanceGrabWizard.");
            }

            ScriptableObject wizard = ScriptableObject.CreateInstance(wizardType);
            try
            {
                SetField(baseWizardType, wizard, "<Target>k__BackingField", baseLink);
                SetField(wizardType, wizard, "_mode", Enum.Parse(FindNestedType(wizardType, "Mode"), "HandToInteractable"));

                Invoke(baseWizardType, wizard, "InitializeFields");
                Invoke(baseWizardType, wizard, "FixMissingDependencies", false);
                if (!(bool)Invoke(baseWizardType, wizard, "CanCreate"))
                {
                    throw new InvalidOperationException("DistanceGrabWizard cannot create the interactable. Check ISDK hand/controller blocks in the scene.");
                }

                Invoke(wizardType, wizard, "Create");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(wizard);
            }
        }

        private static void ConfigureDistanceGrab(GameObject baseLink)
        {
            Rigidbody body = baseLink.GetComponent<Rigidbody>();

            foreach (Grabbable grabbable in baseLink.GetComponentsInChildren<Grabbable>(true))
            {
                GameObject owner = grabbable.gameObject;
                GrabFreeTransformer transformer = owner.GetComponent<GrabFreeTransformer>();
                if (transformer == null)
                {
                    transformer = Undo.AddComponent<GrabFreeTransformer>(owner);
                }

                ConfigureYawOnly(transformer, baseLink.transform.localEulerAngles);

                SerializedObject grabbableObject = new SerializedObject(grabbable);
                SetObject(grabbableObject, "_targetTransform", baseLink.transform);
                SetObject(grabbableObject, "_rigidbody", body);
                SetObject(grabbableObject, "_oneGrabTransformer", transformer);
                SetObject(grabbableObject, "_twoGrabTransformer", transformer);
                SetBool(grabbableObject, "_throwWhenUnselected", false);
                grabbableObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(grabbable);
            }
        }

        private static void ConfigureYawOnly(GrabFreeTransformer transformer, Vector3 localEuler)
        {
            SerializedObject so = new SerializedObject(transformer);
            ConfigureRotationAxis(so, "XAxis", true, localEuler.x, localEuler.x);
            ConfigureRotationAxis(so, "YAxis", false, 0f, 0f);
            ConfigureRotationAxis(so, "ZAxis", true, localEuler.z, localEuler.z);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(transformer);
        }

        private static void ConfigureRotationAxis(SerializedObject so, string axis, bool constrain, float min, float max)
        {
            string path = $"_rotationConstraints.{axis}";
            so.FindProperty($"{path}.ConstrainAxis").boolValue = constrain;
            so.FindProperty($"{path}.AxisRange.Min").floatValue = min;
            so.FindProperty($"{path}.AxisRange.Max").floatValue = max;
        }

        private static bool ConfigureMetaControllerVisuals()
        {
            bool changed = false;
            OVRCameraRig cameraRig = FindSceneComponent<OVRCameraRig>();
            if (cameraRig == null)
            {
                Debug.LogWarning("EyeTracking controller visual setup skipped: cannot find OVRCameraRig.");
                return false;
            }

            Transform leftControllerAnchor = FindChildRecursive(cameraRig.transform, "LeftControllerAnchor");
            Transform rightControllerAnchor = FindChildRecursive(cameraRig.transform, "RightControllerAnchor");
            if (leftControllerAnchor == null || rightControllerAnchor == null)
            {
                Debug.LogWarning("EyeTracking controller visual setup skipped: cannot find controller anchors on OVRCameraRig.");
                return false;
            }

            OVRControllerHelper leftHelper = FindControllerHelper(OVRInput.Controller.LTouch);
            OVRControllerHelper rightHelper = FindControllerHelper(OVRInput.Controller.RTouch);
            changed |= ConfigureControllerHelper(leftHelper, leftControllerAnchor, OVRInput.Controller.LTouch);
            changed |= ConfigureControllerHelper(rightHelper, rightControllerAnchor, OVRInput.Controller.RTouch);

            OVRCameraRigRef cameraRigRef = FindSceneComponent<OVRCameraRigRef>();
            TrackingToWorldTransformerOVR trackingToWorldTransformer = FindSceneComponent<TrackingToWorldTransformerOVR>();
            if (cameraRigRef == null || trackingToWorldTransformer == null)
            {
                Debug.LogWarning("EyeTracking controller visual setup skipped OVR controller data source repair: cannot find OVRCameraRigRef or TrackingToWorldTransformerOVR.");
            }
            else
            {
                foreach (FromOVRControllerDataSource dataSource in FindSceneComponents<FromOVRControllerDataSource>())
                {
                    Handedness handedness = InferHandedness(dataSource.gameObject);
                    SerializedObject dataSourceObject = new SerializedObject(dataSource);
                    changed |= SetObject(dataSourceObject, "_cameraRigRef", cameraRigRef);
                    changed |= SetObject(dataSourceObject, "_trackingToWorldTransformer", trackingToWorldTransformer);
                    changed |= SetEnum(dataSourceObject, "_handedness", handedness);
                    if (dataSourceObject.ApplyModifiedPropertiesWithoutUndo())
                    {
                        changed = true;
                    }
                }
            }

#pragma warning disable CS0618
            foreach (OVRControllerVisual controllerVisual in FindSceneComponents<OVRControllerVisual>())
#pragma warning restore CS0618
            {
                Oculus.Interaction.Input.Controller controller =
                    controllerVisual.GetComponentInParent<Oculus.Interaction.Input.Controller>(true);
                if (controller == null)
                {
                    continue;
                }

                OVRControllerHelper helper = InferHandedness(controller.gameObject) == Handedness.Left
                    ? leftHelper
                    : rightHelper;
                if (helper == null)
                {
                    continue;
                }

                SerializedObject visualObject = new SerializedObject(controllerVisual);
                changed |= SetObject(visualObject, "_controller", controller);
                changed |= SetObject(visualObject, "_ovrControllerHelper", helper);
                if (visualObject.ApplyModifiedPropertiesWithoutUndo())
                {
                    changed = true;
                }
            }

            return changed;
        }

        private static bool ConfigureControllerHelper(
            OVRControllerHelper helper,
            Transform expectedParent,
            OVRInput.Controller controller)
        {
            if (helper == null)
            {
                Debug.LogWarning($"EyeTracking controller visual setup skipped: cannot find {controller} OVRControllerHelper.");
                return false;
            }

            bool changed = false;
            if (helper.m_controller != controller)
            {
                Undo.RecordObject(helper, "Configure controller helper handedness");
                helper.m_controller = controller;
                EditorUtility.SetDirty(helper);
                changed = true;
            }

            if (helper.transform.parent != expectedParent)
            {
                Undo.SetTransformParent(helper.transform, expectedParent, "Parent controller helper to OVRCameraRig anchor");
                helper.transform.localPosition = Vector3.zero;
                helper.transform.localRotation = Quaternion.identity;
                helper.transform.localScale = Vector3.one;
                EditorUtility.SetDirty(helper.transform);
                changed = true;
            }

            changed |= ConfigureControllerHelperModelFallbacks(helper);
            return changed;
        }

        private static bool ConfigureControllerHelperModelFallbacks(OVRControllerHelper helper)
        {
            bool changed = false;
            GameObject placeholder = FindDirectChild(helper.transform, "OVRControllerHelper Missing Model Placeholder")?.gameObject;
            if (placeholder == null)
            {
                placeholder = new GameObject("OVRControllerHelper Missing Model Placeholder");
                placeholder.SetActive(false);
                Undo.RegisterCreatedObjectUndo(placeholder, "Create controller helper model placeholder");
                Undo.SetTransformParent(placeholder.transform, helper.transform, "Parent controller helper model placeholder");
                placeholder.transform.localPosition = Vector3.zero;
                placeholder.transform.localRotation = Quaternion.identity;
                placeholder.transform.localScale = Vector3.one;
                EditorUtility.SetDirty(placeholder);
                changed = true;
            }
            else if (placeholder.activeSelf)
            {
                Undo.RecordObject(placeholder, "Disable controller helper model placeholder");
                placeholder.SetActive(false);
                EditorUtility.SetDirty(placeholder);
                changed = true;
            }

            bool helperChanged = false;
            helperChanged |= SetMissingControllerModel(ref helper.m_modelOculusTouchQuestAndRiftSLeftController, placeholder);
            helperChanged |= SetMissingControllerModel(ref helper.m_modelOculusTouchQuestAndRiftSRightController, placeholder);
            helperChanged |= SetMissingControllerModel(ref helper.m_modelOculusTouchRiftLeftController, placeholder);
            helperChanged |= SetMissingControllerModel(ref helper.m_modelOculusTouchRiftRightController, placeholder);
            helperChanged |= SetMissingControllerModel(ref helper.m_modelOculusTouchQuest2LeftController, placeholder);
            helperChanged |= SetMissingControllerModel(ref helper.m_modelOculusTouchQuest2RightController, placeholder);
            helperChanged |= SetMissingControllerModel(ref helper.m_modelMetaTouchProLeftController, placeholder);
            helperChanged |= SetMissingControllerModel(ref helper.m_modelMetaTouchProRightController, placeholder);
            helperChanged |= SetMissingControllerModel(ref helper.m_modelMetaTouchPlusLeftController, placeholder);
            helperChanged |= SetMissingControllerModel(ref helper.m_modelMetaTouchPlusRightController, placeholder);
            if (helperChanged)
            {
                Undo.RecordObject(helper, "Repair controller helper model references");
                EditorUtility.SetDirty(helper);
                changed = true;
            }

            return changed;
        }

        private static bool SetMissingControllerModel(ref GameObject model, GameObject placeholder)
        {
            if (model != null)
            {
                return false;
            }

            model = placeholder;
            return true;
        }

        private static bool ConfigureMetaControllerInteractors()
        {
            Type utilsType = FindType("Oculus.Interaction.Editor.QuickActions.InteractorUtils");
            Type interactorTypesType = FindType("Oculus.Interaction.Editor.QuickActions.InteractorTypes");
            Type deviceTypesType = FindType("Oculus.Interaction.Editor.QuickActions.DeviceTypes");
            if (utilsType == null || interactorTypesType == null || deviceTypesType == null)
            {
                Debug.LogWarning("EyeTracking controller interactor setup skipped: cannot find Interaction SDK QuickActions.");
                return false;
            }

            object interactorTypes = Enum.Parse(interactorTypesType, "Ray, DistanceGrab");
            object deviceTypes = Enum.Parse(deviceTypesType, "Controllers");
            object added = Invoke(utilsType, null, "AddInteractorsToRig", interactorTypes, deviceTypes);

            int addedCount = 0;
            if (added is System.Collections.IEnumerable addedObjects)
            {
                foreach (object addedObject in addedObjects)
                {
                    if (addedObject is GameObject gameObject)
                    {
                        EditorUtility.SetDirty(gameObject);
                        addedCount++;
                    }
                }
            }

            return addedCount > 0;
        }

        private static bool ConfigureControllerDistanceGrabSelectors()
        {
            bool changed = false;
            foreach (DistanceGrabInteractor interactor in FindSceneComponents<DistanceGrabInteractor>())
            {
                if (interactor.GetComponent<ControllerRef>() == null)
                {
                    continue;
                }

                SerializedObject interactorObject = new SerializedObject(interactor);
                SerializedProperty selectorProperty = interactorObject.FindProperty("_selector");
                ControllerSelector selector = selectorProperty?.objectReferenceValue as ControllerSelector;
                if (selector == null)
                {
                    continue;
                }

                SerializedObject selectorObject = new SerializedObject(selector);
                changed |= SetInt(selectorObject, "_controllerButtonUsage", (int)ControllerButtonUsage.TriggerButton);
                if (selectorObject.ApplyModifiedPropertiesWithoutUndo())
                {
                    changed = true;
                }

                if (selector.gameObject.name != "TriggerSelector")
                {
                    Undo.RecordObject(selector.gameObject, "Rename controller distance grab selector");
                    selector.gameObject.name = "TriggerSelector";
                    EditorUtility.SetDirty(selector.gameObject);
                    changed = true;
                }
            }

            return changed;
        }

        private static bool ConfigureLegacyXrTemplateControllerObjects()
        {
            bool changed = false;
            string[] childNamesToDisable = { "Offset", "Ray", "Poke" };
            foreach (string objectName in new[] { "Left Hand", "Right Hand" })
            {
                GameObject legacyHand = FindSceneObject(objectName);
                if (legacyHand == null || !LooksLikeLegacyXrTemplateObject(legacyHand))
                {
                    continue;
                }

                foreach (Transform child in legacyHand.GetComponentsInChildren<Transform>(true)
                             .Where(child => child != legacyHand.transform && childNamesToDisable.Contains(child.name)))
                {
                    if (!child.gameObject.activeSelf)
                    {
                        continue;
                    }

                    Undo.RecordObject(child.gameObject, "Disable legacy XRTemplate controller object");
                    child.gameObject.SetActive(false);
                    EditorUtility.SetDirty(child.gameObject);
                    changed = true;
                }
            }

            GameObject legacyOrigin = FindSceneObject("XR Origin");
            if (legacyOrigin != null && legacyOrigin.activeSelf && LooksLikeLegacyXrTemplateObject(legacyOrigin))
            {
                Undo.RecordObject(legacyOrigin, "Disable legacy XRTemplate origin object");
                legacyOrigin.SetActive(false);
                EditorUtility.SetDirty(legacyOrigin);
                changed = true;
            }

            return changed;
        }

        private static bool LooksLikeLegacyXrTemplateObject(GameObject gameObject)
        {
            return HasBehaviourNamed(gameObject, "TrackedPoseDriver")
                || HasBehaviourNamed(gameObject, "XROrigin")
                || HasBehaviourNamed(gameObject, "HandedHierarchy");
        }

        private static bool HasBehaviourNamed(GameObject gameObject, string typeName)
        {
            return gameObject.GetComponents<MonoBehaviour>()
                .Any(behaviour => behaviour != null && behaviour.GetType().Name == typeName);
        }

        private static OVRControllerHelper FindControllerHelper(OVRInput.Controller controller)
        {
            return FindSceneComponents<OVRControllerHelper>()
                .FirstOrDefault(helper => helper.m_controller == controller);
        }

        private static Handedness InferHandedness(GameObject gameObject)
        {
            Transform current = gameObject.transform;
            while (current != null)
            {
                if (current.name.IndexOf("Left", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return Handedness.Left;
                }

                if (current.name.IndexOf("Right", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return Handedness.Right;
                }

                current = current.parent;
            }

            return Handedness.Right;
        }

        private static Transform FindChildRecursive(Transform parent, string childName)
        {
            if (parent.name == childName)
            {
                return parent;
            }

            foreach (Transform child in parent)
            {
                Transform result = FindChildRecursive(child, childName);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private static Transform FindDirectChild(Transform parent, string childName)
        {
            foreach (Transform child in parent)
            {
                if (child.name == childName)
                {
                    return child;
                }
            }

            return null;
        }

        private static T FindSceneComponent<T>() where T : Component
        {
            return FindSceneComponents<T>().FirstOrDefault();
        }

        private static IEnumerable<T> FindSceneComponents<T>() where T : Component
        {
            Scene activeScene = SceneManager.GetActiveScene();
            return Resources.FindObjectsOfTypeAll<T>()
                .Where(component => component != null && component.gameObject.scene == activeScene);
        }

        private static async Task<bool> InstallBlockIfMissing(string blockId)
        {
            if (IsBlockPresent(blockId))
            {
                return false;
            }

            Type utilsType = FindType("Meta.XR.BuildingBlocks.Editor.Utils");
            MethodInfo getBlockData = utilsType?.GetMethod("GetBlockData", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(string) }, null);
            object blockData = getBlockData?.Invoke(null, new object[] { blockId });
            if (blockData == null)
            {
                throw new InvalidOperationException($"Cannot find Building Block data: {blockId}");
            }

            MethodInfo installMethod = FindMethod(blockData.GetType(), "InstallWithDependencies", typeof(GameObject));
            Task installTask = (Task)installMethod.Invoke(blockData, new object[] { null });
            await installTask;
            return true;
        }

        private static bool IsBlockPresent(string blockId)
        {
            if (blockId == CoreControllerTrackingBlockId)
            {
                IEnumerable<OVRControllerHelper> helpers = FindSceneComponents<OVRControllerHelper>();
                return helpers.Any(helper => helper.m_controller == OVRInput.Controller.LTouch)
                    && helpers.Any(helper => helper.m_controller == OVRInput.Controller.RTouch);
            }

            return FindSceneComponents<BuildingBlock>().Any(block => block.BlockId == blockId);
        }

        private static GameObject FindSceneObject(string objectName)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            return Resources.FindObjectsOfTypeAll<GameObject>()
                .FirstOrDefault(go => go.name == objectName && go.scene == activeScene);
        }

        private static Type FindType(string fullName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(fullName))
                .FirstOrDefault(type => type != null);
        }

        private static Type FindNestedType(Type type, string name)
        {
            Type nested = type.GetNestedType(name, BindingFlags.Public | BindingFlags.NonPublic);
            if (nested == null)
            {
                throw new MissingMemberException(type.FullName, name);
            }

            return nested;
        }

        private static MethodInfo FindMethod(Type type, string name, params Type[] parameterTypes)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                MethodInfo method = current.GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, parameterTypes, null);
                if (method != null)
                {
                    return method;
                }
            }

            throw new MissingMethodException(type.FullName, name);
        }

        private static object Invoke(Type type, object target, string methodName, params object[] args)
        {
            MethodInfo method = FindMethod(type, methodName, args.Select(arg => arg?.GetType() ?? typeof(object)).ToArray());
            return method.Invoke(target, args);
        }

        private static void SetField(Type type, object target, string fieldName, object value)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    field.SetValue(target, value);
                    return;
                }
            }

            throw new MissingFieldException(type.FullName, fieldName);
        }

        private static bool SetObject(SerializedObject obj, string propertyName, UnityEngine.Object value)
        {
            SerializedProperty property = obj.FindProperty(propertyName);
            if (property != null && property.objectReferenceValue != value)
            {
                property.objectReferenceValue = value;
                return true;
            }

            return false;
        }

        private static bool SetBool(SerializedObject obj, string propertyName, bool value)
        {
            SerializedProperty property = obj.FindProperty(propertyName);
            if (property != null && property.boolValue != value)
            {
                property.boolValue = value;
                return true;
            }

            return false;
        }

        private static bool SetInt(SerializedObject obj, string propertyName, int value)
        {
            SerializedProperty property = obj.FindProperty(propertyName);
            if (property != null && property.intValue != value)
            {
                property.intValue = value;
                return true;
            }

            return false;
        }

        private static bool SetEnum(SerializedObject obj, string propertyName, Enum value)
        {
            SerializedProperty property = obj.FindProperty(propertyName);
            int intValue = Convert.ToInt32(value);
            if (property != null && property.enumValueIndex != intValue)
            {
                property.enumValueIndex = intValue;
                return true;
            }

            return false;
        }
    }
}
#endif
