using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace Realmz.EditorTools
{
    /// <summary>
    /// Builds Assets/Audio/RealmzMasterMixer.mixer via UnityEditor internal AudioMixerController
    /// APIs with Music, SFX, Ambience, and UI routing groups, exposed volume parameters matching
    /// BywaterAudioDirector, an Ambience Lowpass filter, and Overworld/Dungeon snapshots.
    /// </summary>
    public static class RealmzAudioMixerBuilder
    {
        public const string MixerAssetPath = "Assets/Audio/RealmzMasterMixer.mixer";

        private const string MusicGroupName = "Music";
        private const string SfxGroupName = "SFX";
        private const string AmbienceGroupName = "Ambience";
        private const string UiGroupName = "UI";

        private const string MusicVolumeParam = "MusicVolumeDb";
        private const string SfxVolumeParam = "SfxVolumeDb";
        private const string AmbienceVolumeParam = "AmbienceVolumeDb";
        private const string UiVolumeParam = "UiVolumeDb";
        private const string AmbienceLowpassParam = "AmbienceLowpassCutoffHz";

        private const string OverworldSnapshotName = "Overworld";
        private const string DungeonSnapshotName = "Dungeon";

        private const BindingFlags AnyBinding =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        [MenuItem("Realmz/Audio/Build Master AudioMixer")]
        public static AudioMixer BuildMasterMixer()
        {
            EnsureFolder("Assets/Audio");

            Type controllerType = FindEditorType("UnityEditor.Audio.AudioMixerController");
            Type groupType = FindEditorType("UnityEditor.Audio.AudioMixerGroupController");
            Type effectType = FindEditorType("UnityEditor.Audio.AudioMixerEffectController");
            Type snapshotType = FindEditorType("UnityEditor.Audio.AudioMixerSnapshotController");
            Type exposedParamType = FindEditorType("UnityEditor.Audio.ExposedAudioParameter");

            if (controllerType == null || groupType == null || effectType == null || snapshotType == null)
            {
                Debug.LogError("[RealmzAudioMixerBuilder] Failed to locate UnityEditor.Audio mixer controller types via reflection.");
                return null;
            }

            UnityEngine.Object controller = AssetDatabase.LoadAssetAtPath(MixerAssetPath, controllerType);
            if (controller == null)
            {
                MethodInfo createMixerMethod = controllerType.GetMethod(
                    "CreateMixerControllerAtPath",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (createMixerMethod == null)
                {
                    Debug.LogError("[RealmzAudioMixerBuilder] AudioMixerController.CreateMixerControllerAtPath not found.");
                    return null;
                }

                controller = createMixerMethod.Invoke(null, new object[] { MixerAssetPath }) as UnityEngine.Object;
            }

            if (controller == null)
            {
                Debug.LogError("[RealmzAudioMixerBuilder] Failed to create or load AudioMixerController at " + MixerAssetPath);
                return null;
            }

            UnityEngine.Object masterGroup = GetMemberValue(controller, controllerType, "masterGroup") as UnityEngine.Object;
            if (masterGroup == null)
            {
                Debug.LogError("[RealmzAudioMixerBuilder] masterGroup was null on AudioMixerController.");
                return controller as AudioMixer;
            }

            UnityEngine.Object musicGroup = EnsureChildGroup(controller, controllerType, groupType, masterGroup, MusicGroupName);
            UnityEngine.Object sfxGroup = EnsureChildGroup(controller, controllerType, groupType, masterGroup, SfxGroupName);
            UnityEngine.Object ambienceGroup = EnsureChildGroup(controller, controllerType, groupType, masterGroup, AmbienceGroupName);
            UnityEngine.Object uiGroup = EnsureChildGroup(controller, controllerType, groupType, masterGroup, UiGroupName);

            EnsureExposedVolumeParameter(controller, controllerType, groupType, exposedParamType, musicGroup, MusicVolumeParam);
            EnsureExposedVolumeParameter(controller, controllerType, groupType, exposedParamType, sfxGroup, SfxVolumeParam);
            EnsureExposedVolumeParameter(controller, controllerType, groupType, exposedParamType, ambienceGroup, AmbienceVolumeParam);
            EnsureExposedVolumeParameter(controller, controllerType, groupType, exposedParamType, uiGroup, UiVolumeParam);

            UnityEngine.Object lowpassEffect = EnsureGroupEffect(controller, controllerType, groupType, effectType, ambienceGroup, ResolveLowpassEffectName());
            if (lowpassEffect != null)
            {
                EnsureExposedEffectParameter(
                    controller,
                    controllerType,
                    effectType,
                    exposedParamType,
                    lowpassEffect,
                    AmbienceLowpassParam,
                    "Cutoff freq",
                    "Cutoff");
            }

            UnityEngine.Object overworldSnapshot = EnsureSnapshot(
                controller,
                controllerType,
                snapshotType,
                OverworldSnapshotName,
                renameDefaultSnapshot: true,
                cloneFromSnapshot: null);

            UnityEngine.Object dungeonSnapshot = EnsureSnapshot(
                controller,
                controllerType,
                snapshotType,
                DungeonSnapshotName,
                renameDefaultSnapshot: false,
                cloneFromSnapshot: overworldSnapshot);

            if (overworldSnapshot != null)
            {
                SetGroupVolume(controllerType, groupType, snapshotType, controller, ambienceGroup, overworldSnapshot, 0f);
                if (lowpassEffect != null)
                {
                    SetEffectParameterValue(controllerType, effectType, snapshotType, controller, lowpassEffect, overworldSnapshot, 22000f, "Cutoff freq", "Cutoff");
                }
            }

            if (dungeonSnapshot != null)
            {
                SetGroupVolume(controllerType, groupType, snapshotType, controller, ambienceGroup, dungeonSnapshot, -4f);
                if (lowpassEffect != null)
                {
                    SetEffectParameterValue(controllerType, effectType, snapshotType, controller, lowpassEffect, dungeonSnapshot, 1200f, "Cutoff freq", "Cutoff");
                }
            }

            if (overworldSnapshot != null)
            {
                SetMemberValue(controller, controllerType, "TargetSnapshot", overworldSnapshot);
                SetMemberValue(controller, controllerType, "startSnapshot", overworldSnapshot);
            }

            InvokeOptional(controller, controllerType, "SanitizeGroupViews");
            InvokeOptional(controller, controllerType, "OnChangedExposedParameter");
            InvokeOptional(controller, controllerType, "OnMixerControllerChanged");

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[RealmzAudioMixerBuilder] Built RealmzMasterMixer at " + MixerAssetPath);
            return controller as AudioMixer;
        }

        private static UnityEngine.Object EnsureChildGroup(
            UnityEngine.Object controller,
            Type controllerType,
            Type groupType,
            UnityEngine.Object masterGroup,
            string groupName)
        {
            UnityEngine.Object existing = FindGroupByName(controller, controllerType, groupName);
            if (existing != null)
            {
                return existing;
            }

            UnityEngine.Object created = InvokeMethodVariants(
                controller,
                controllerType,
                "CreateNewGroup",
                new object[] { groupName, false },
                new object[] { groupName }) as UnityEngine.Object;

            if (created == null)
            {
                Debug.LogWarning("[RealmzAudioMixerBuilder] Could not create AudioMixer group '" + groupName + "'.");
                return null;
            }

            InvokeMethodVariants(
                controller,
                controllerType,
                "AddChildToParent",
                new object[] { created, masterGroup });

            Array views = GetMemberValue(controller, controllerType, "views") as Array;
            object viewIndexObj = GetMemberValue(controller, controllerType, "currentViewIndex");
            int viewIdx = viewIndexObj is int idx ? idx : 0;
            if (views != null && viewIdx >= 0 && viewIdx < views.Length)
            {
                try
                {
                    InvokeMethodVariants(
                        controller,
                        controllerType,
                        "AddGroupToCurrentView",
                        new object[] { created });
                }
                catch (Exception)
                {
                    // Batchmode controllers created without an open window have no UI views yet.
                }
            }

            return created;
        }

        private static UnityEngine.Object FindGroupByName(UnityEngine.Object controller, Type controllerType, string groupName)
        {
            Array allGroups = GetMemberValue(controller, controllerType, "AllGroups") as Array;
            if (allGroups == null)
            {
                return null;
            }

            for (int i = 0; i < allGroups.Length; i++)
            {
                UnityEngine.Object g = allGroups.GetValue(i) as UnityEngine.Object;
                if (g != null && string.Equals(g.name, groupName, StringComparison.OrdinalIgnoreCase))
                {
                    return g;
                }
            }

            return null;
        }

        private static UnityEngine.Object EnsureGroupEffect(
            UnityEngine.Object controller,
            Type controllerType,
            Type groupType,
            Type effectType,
            UnityEngine.Object group,
            string effectName)
        {
            if (group == null)
            {
                return null;
            }

            Array existingEffects = GetMemberValue(group, groupType, "effects") as Array;
            if (existingEffects != null)
            {
                for (int i = 0; i < existingEffects.Length; i++)
                {
                    UnityEngine.Object eff = existingEffects.GetValue(i) as UnityEngine.Object;
                    if (eff == null)
                    {
                        continue;
                    }

                    string existingName = GetMemberValue(eff, effectType, "effectName") as string ?? eff.name;
                    if (string.Equals(existingName, effectName, StringComparison.OrdinalIgnoreCase))
                    {
                        return eff;
                    }
                }
            }

            UnityEngine.Object newEffect = null;
            try
            {
                newEffect = Activator.CreateInstance(effectType, new object[] { effectName }) as UnityEngine.Object;
            }
            catch (Exception)
            {
                newEffect = InvokeMethodVariants(
                    effectType,
                    "Internal_CreateAudioMixerEffectController",
                    new object[] { effectName }) as UnityEngine.Object;
            }

            if (newEffect == null)
            {
                Debug.LogWarning("[RealmzAudioMixerBuilder] Could not instantiate effect '" + effectName + "'.");
                return null;
            }

            newEffect.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;
            InvokeOptional(newEffect, effectType, "PreallocateGUIDs");

            int insertIndex = existingEffects != null ? existingEffects.Length : 0;
            bool invokedInsert = TryInvokeMethodVariants(
                group,
                groupType,
                "InsertEffect",
                out _,
                new object[] { newEffect, insertIndex });

            if (!AssetDatabase.Contains(newEffect))
            {
                TryInvokeMethodVariants(controller, controllerType, "AddNewSubAsset", out _, new object[] { newEffect, false });
            }
            if (!AssetDatabase.Contains(newEffect))
            {
                AssetDatabase.AddObjectToAsset(newEffect, controller);
            }

            if (!invokedInsert || FindEffectOnGroup(group, groupType, effectType, effectName) == null)
            {
                if (!TryInvokeMethodVariants(controller, controllerType, "AddEffect", out _, new object[] { group, newEffect }))
                {
                    TryInvokeMethodVariants(group, groupType, "AddEffect", out _, new object[] { newEffect });
                }
            }

            EditorUtility.SetDirty(newEffect);
            EditorUtility.SetDirty(group);
            return FindEffectOnGroup(group, groupType, effectType, effectName) ?? newEffect;
        }

        private static string ResolveLowpassEffectName()
        {
            Type defsType = FindEditorType("UnityEditor.Audio.MixerEffectDefinitions");
            if (defsType != null)
            {
                string[] candidates = { "Lowpass Simple", "Lowpass", "AudioLowPassFilter" };
                for (int i = 0; i < candidates.Length; i++)
                {
                    if (InvokeMethodVariants(defsType, "EffectExists", new object[] { candidates[i] }) is bool exists && exists)
                    {
                        return candidates[i];
                    }
                }

                if (InvokeMethodVariants(defsType, "GetAudioEffectNames") is string[] allNames)
                {
                    for (int i = 0; i < allNames.Length; i++)
                    {
                        if (!string.IsNullOrEmpty(allNames[i]) &&
                            allNames[i].IndexOf("lowpass", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return allNames[i];
                        }
                    }
                }
            }

            return "Lowpass Simple";
        }

        private static UnityEngine.Object FindEffectOnGroup(
            UnityEngine.Object group,
            Type groupType,
            Type effectType,
            string effectName)
        {
            Array effects = GetMemberValue(group, groupType, "effects") as Array;
            if (effects == null)
            {
                return null;
            }

            for (int i = 0; i < effects.Length; i++)
            {
                UnityEngine.Object eff = effects.GetValue(i) as UnityEngine.Object;
                if (eff == null)
                {
                    continue;
                }

                string existingName = GetMemberValue(eff, effectType, "effectName") as string ?? eff.name;
                if (string.Equals(existingName, effectName, StringComparison.OrdinalIgnoreCase))
                {
                    return eff;
                }
            }

            return null;
        }

        private static void EnsureExposedVolumeParameter(
            UnityEngine.Object controller,
            Type controllerType,
            Type groupType,
            Type exposedParamType,
            UnityEngine.Object group,
            string exposedName)
        {
            if (group == null)
            {
                return;
            }

            object guid = InvokeMethodVariants(group, groupType, "GetGUIDForVolume");
            if (guid == null)
            {
                Debug.LogWarning("[RealmzAudioMixerBuilder] GetGUIDForVolume returned null for " + group.name);
                return;
            }

            EnsureExposedGuid(controller, controllerType, exposedParamType, guid, exposedName);
        }

        private static void EnsureExposedEffectParameter(
            UnityEngine.Object controller,
            Type controllerType,
            Type effectType,
            Type exposedParamType,
            UnityEngine.Object effect,
            string exposedName,
            params string[] candidateParamNames)
        {
            if (effect == null)
            {
                return;
            }

            object guid = null;
            for (int i = 0; i < candidateParamNames.Length; i++)
            {
                guid = InvokeMethodVariants(effect, effectType, "GetGUIDForParameter", new object[] { candidateParamNames[i] });
                if (guid != null && !IsEmptyGuid(guid))
                {
                    break;
                }
            }

            if (guid == null || IsEmptyGuid(guid))
            {
                return;
            }

            EnsureExposedGuid(controller, controllerType, exposedParamType, guid, exposedName);
        }

        private static void EnsureExposedGuid(
            UnityEngine.Object controller,
            Type controllerType,
            Type exposedParamType,
            object guid,
            string exposedName)
        {
            if (exposedParamType == null || guid == null)
            {
                return;
            }

            object containsObj = InvokeMethodVariants(controller, controllerType, "ContainsExposedParameter", new object[] { guid });
            bool alreadyExposed = containsObj is bool b && b;
            if (alreadyExposed)
            {
                Array existingParams = GetMemberValue(controller, controllerType, "exposedParameters") as Array;
                if (existingParams != null)
                {
                    for (int i = 0; i < existingParams.Length; i++)
                    {
                        object entry = existingParams.GetValue(i);
                        object entryGuid = GetMemberValue(entry, exposedParamType, "guid");
                        if (Equals(entryGuid, guid))
                        {
                            string currentName = GetMemberValue(entry, exposedParamType, "name") as string;
                            if (!string.Equals(currentName, exposedName, StringComparison.Ordinal))
                            {
                                InvokeMethodVariants(controller, controllerType, "RenameParameter", new object[] { guid, exposedName });
                            }
                            return;
                        }
                    }
                }
                return;
            }

            object exposedParam = Activator.CreateInstance(exposedParamType);
            SetMemberValue(exposedParam, exposedParamType, "guid", guid);
            SetMemberValue(exposedParam, exposedParamType, "name", exposedName);

            bool invokedAdd = TryInvokeMethodVariants(controller, controllerType, "AddExposedParameter", out _, new object[] { exposedParam });
            object postAddContains = invokedAdd
                ? InvokeMethodVariants(controller, controllerType, "ContainsExposedParameter", new object[] { guid })
                : null;
            bool isNowExposed = postAddContains is bool postB && postB;

            if (!invokedAdd || !isNowExposed)
            {
                Array current = GetMemberValue(controller, controllerType, "exposedParameters") as Array;
                int len = current != null ? current.Length : 0;
                Array resized = Array.CreateInstance(exposedParamType, len + 1);
                if (current != null && len > 0)
                {
                    Array.Copy(current, resized, len);
                }
                resized.SetValue(exposedParam, len);
                SetMemberValue(controller, controllerType, "exposedParameters", resized);
            }
        }

        private static UnityEngine.Object EnsureSnapshot(
            UnityEngine.Object controller,
            Type controllerType,
            Type snapshotType,
            string snapshotName,
            bool renameDefaultSnapshot,
            UnityEngine.Object cloneFromSnapshot)
        {
            Array snapshots = GetMemberValue(controller, controllerType, "snapshots") as Array;
            if (snapshots != null)
            {
                for (int i = 0; i < snapshots.Length; i++)
                {
                    UnityEngine.Object snap = snapshots.GetValue(i) as UnityEngine.Object;
                    if (snap != null && string.Equals(snap.name, snapshotName, StringComparison.OrdinalIgnoreCase))
                    {
                        return snap;
                    }
                }

                if (renameDefaultSnapshot && snapshots.Length == 1)
                {
                    UnityEngine.Object firstSnap = snapshots.GetValue(0) as UnityEngine.Object;
                    if (firstSnap != null)
                    {
                        firstSnap.name = snapshotName;
                        EditorUtility.SetDirty(firstSnap);
                        return firstSnap;
                    }
                }
            }

            int beforeCount = snapshots != null ? snapshots.Length : 0;
            if (cloneFromSnapshot != null)
            {
                SetMemberValue(controller, controllerType, "TargetSnapshot", cloneFromSnapshot);
            }

            InvokeMethodVariants(
                controller,
                controllerType,
                "CloneNewSnapshotFromTarget",
                new object[] { false },
                Array.Empty<object>());

            Array afterSnapshots = GetMemberValue(controller, controllerType, "snapshots") as Array;
            if (afterSnapshots != null && afterSnapshots.Length > beforeCount)
            {
                UnityEngine.Object newSnap = afterSnapshots.GetValue(afterSnapshots.Length - 1) as UnityEngine.Object;
                if (newSnap != null)
                {
                    newSnap.name = snapshotName;
                    EditorUtility.SetDirty(newSnap);
                    return newSnap;
                }
            }

            return null;
        }

        private static void SetGroupVolume(
            Type controllerType,
            Type groupType,
            Type snapshotType,
            UnityEngine.Object controller,
            UnityEngine.Object group,
            UnityEngine.Object snapshot,
            float volumeDb)
        {
            if (group == null || snapshot == null)
            {
                return;
            }

            InvokeMethodVariants(
                group,
                groupType,
                "SetValueForVolume",
                new object[] { controller, snapshot, volumeDb });
        }

        private static void SetEffectParameterValue(
            Type controllerType,
            Type effectType,
            Type snapshotType,
            UnityEngine.Object controller,
            UnityEngine.Object effect,
            UnityEngine.Object snapshot,
            float value,
            params string[] candidateParamNames)
        {
            if (effect == null || snapshot == null)
            {
                return;
            }

            for (int i = 0; i < candidateParamNames.Length; i++)
            {
                object guid = InvokeMethodVariants(effect, effectType, "GetGUIDForParameter", new object[] { candidateParamNames[i] });
                if (guid == null || IsEmptyGuid(guid))
                {
                    continue;
                }

                InvokeMethodVariants(
                    effect,
                    effectType,
                    "SetValueForParameter",
                    new object[] { controller, snapshot, candidateParamNames[i], value });
                return;
            }
        }

        private static bool IsEmptyGuid(object guid)
        {
            if (guid == null)
            {
                return true;
            }

            MethodInfo emptyMethod = guid.GetType().GetMethod("Empty", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (emptyMethod != null && emptyMethod.ReturnType == typeof(bool))
            {
                return (bool)emptyMethod.Invoke(guid, null);
            }

            string text = guid.ToString();
            return string.IsNullOrEmpty(text) || string.Equals(text, "00000000000000000000000000000000", StringComparison.Ordinal);
        }

        private static Type FindEditorType(string fullName)
        {
            Assembly editorAsm = typeof(Editor).Assembly;
            Type t = editorAsm.GetType(fullName, throwOnError: false);
            if (t != null)
            {
                return t;
            }

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                t = assemblies[i].GetType(fullName, throwOnError: false);
                if (t != null)
                {
                    return t;
                }
            }

            return null;
        }

        private static object GetMemberValue(object target, Type targetType, string memberName)
        {
            if (target == null || targetType == null)
            {
                return null;
            }

            PropertyInfo prop = targetType.GetProperty(memberName, AnyBinding);
            if (prop != null)
            {
                return prop.GetValue(target, null);
            }

            FieldInfo field = targetType.GetField(memberName, AnyBinding);
            if (field != null)
            {
                return field.GetValue(target);
            }

            return null;
        }

        private static void SetMemberValue(object target, Type targetType, string memberName, object value)
        {
            if (target == null || targetType == null)
            {
                return;
            }

            PropertyInfo prop = targetType.GetProperty(memberName, AnyBinding);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(target, value, null);
                return;
            }

            FieldInfo field = targetType.GetField(memberName, AnyBinding);
            if (field != null)
            {
                field.SetValue(target, value);
            }
        }

        private static void InvokeOptional(object target, Type targetType, string methodName)
        {
            MethodInfo method = targetType.GetMethod(methodName, AnyBinding);
            if (method != null && method.GetParameters().Length == 0)
            {
                method.Invoke(target, null);
            }
        }

        private static object InvokeMethodVariants(Type staticType, string methodName, params object[][] argVariants)
        {
            return InvokeMethodVariants(null, staticType, methodName, argVariants);
        }

        private static object InvokeMethodVariants(
            object target,
            Type targetType,
            string methodName,
            params object[][] argVariants)
        {
            TryInvokeMethodVariants(target, targetType, methodName, out object result, argVariants);
            return result;
        }

        private static bool TryInvokeMethodVariants(
            object target,
            Type targetType,
            string methodName,
            out object result,
            params object[][] argVariants)
        {
            result = null;
            if (targetType == null)
            {
                return false;
            }

            MethodInfo[] methods = targetType.GetMethods(AnyBinding);
            if (argVariants == null || argVariants.Length == 0)
            {
                argVariants = new[] { Array.Empty<object>() };
            }

            for (int v = 0; v < argVariants.Length; v++)
            {
                object[] args = argVariants[v] ?? Array.Empty<object>();
                for (int m = 0; m < methods.Length; m++)
                {
                    MethodInfo method = methods[m];
                    if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length != args.Length)
                    {
                        continue;
                    }

                    if (!ParametersMatch(parameters, args))
                    {
                        continue;
                    }

                    result = method.Invoke(target, args);
                    return true;
                }
            }

            return false;
        }

        private static bool ParametersMatch(ParameterInfo[] parameters, object[] args)
        {
            for (int i = 0; i < parameters.Length; i++)
            {
                object arg = args[i];
                Type paramType = parameters[i].ParameterType;
                if (arg == null)
                {
                    if (paramType.IsValueType && Nullable.GetUnderlyingType(paramType) == null)
                    {
                        return false;
                    }
                    continue;
                }

                if (!paramType.IsInstanceOfType(arg))
                {
                    return false;
                }
            }

            return true;
        }

        private static void EnsureFolder(string assetPath)
        {
            string[] parts = assetPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }
    }
}
