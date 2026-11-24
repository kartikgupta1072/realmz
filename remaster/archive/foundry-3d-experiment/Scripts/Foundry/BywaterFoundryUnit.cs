using System;
using UnityEngine;

namespace Realmz.Foundry
{
    /// <summary>
    /// Drives shipped Realmz Asset Foundry 3D or HD-2D prefabs in play mode,
    /// bridging Animator/Animation clips with deterministic procedural motion.
    /// </summary>
    public sealed class BywaterFoundryUnit : MonoBehaviour
    {
        public const string StateIdle = "Idle";
        public const string StateMove = "Move";
        public const string StateAttack = "Attack";

        [Header("Foundry Identity")]
        public string rigProfile = "Static";
        public string category = "Prop";
        public bool enableStaticWindSway = true;

        [Header("Scene References")]
        public Transform visualRoot;
        public Animator animator;
        public Animation legacyAnimation;

        [Header("Runtime State")]
        public string currentState = StateIdle;

        private float _phase;
        private float _attackTimer;
        private float _attackDuration = 0.34f;
        private Vector3 _baseLocalPosition;
        private Quaternion _baseLocalRotation = Quaternion.identity;
        private Vector3 _baseLocalScale = Vector3.one;
        private bool _hasCachedBase;
        private Transform _tailBone;

        private void Awake()
        {
            ResolveReferences();
            CacheBasePose();
            ComputeDeterministicPhase();
        }

        private void OnDisable()
        {
            RestoreBasePose();
        }

        public void SetState(string state)
        {
            ResolveReferences();
            CacheBasePose();

            string normalized = NormalizeState(state);
            currentState = normalized;

            ApplyClipState(normalized);

            if (string.Equals(normalized, StateAttack, StringComparison.Ordinal))
            {
                PlayAttackImpulse();
            }
        }

        public void PlayAttackImpulse()
        {
            ResolveReferences();
            CacheBasePose();
            _attackTimer = _attackDuration;

            if (animator != null && animator.runtimeAnimatorController != null && animator.isInitialized)
            {
                TryTriggerAnimator(StateAttack);
            }

            if (legacyAnimation != null && legacyAnimation.GetClip(StateAttack) != null)
            {
                legacyAnimation.CrossFade(StateAttack, 0.08f);
            }
        }

        private void Update()
        {
            if (visualRoot == null)
            {
                ResolveReferences();
                if (visualRoot == null)
                {
                    return;
                }
            }

            if (!_hasCachedBase)
            {
                CacheBasePose();
            }

            float dt = Time.deltaTime;
            bool isMoving = string.Equals(currentState, StateMove, StringComparison.Ordinal);
            float time = Time.time;

            Vector3 posOffset = Vector3.zero;
            Vector3 rotOffsetDeg = Vector3.zero;
            Vector3 scaleMult = Vector3.one;

            string profile = rigProfile ?? "Static";
            if (string.Equals(profile, "Aquatic", StringComparison.OrdinalIgnoreCase))
            {
                float speed = isMoving ? 4.4f : 2.2f;
                float t = time * speed + _phase;
                float yawAmp = isMoving ? 9.5f : 5.0f;
                float rollAmp = isMoving ? 4.0f : 2.0f;
                float bobAmp = isMoving ? 0.055f : 0.032f;

                rotOffsetDeg.y = Mathf.Sin(t) * yawAmp;
                rotOffsetDeg.z = Mathf.Cos(t * 0.85f) * rollAmp;
                posOffset.y = Mathf.Sin(t * 1.35f + 0.4f) * bobAmp;

                if (_tailBone != null)
                {
                    _tailBone.localRotation = Quaternion.Euler(0f, Mathf.Sin(t * 1.8f + 1.1f) * (yawAmp * 1.8f), 0f);
                }
            }
            else if (string.Equals(profile, "Humanoid", StringComparison.OrdinalIgnoreCase)
                || string.Equals(profile, "Quadruped", StringComparison.OrdinalIgnoreCase))
            {
                float breathSpeed = isMoving ? 5.6f : 2.3f;
                float t = time * breathSpeed + _phase;

                if (isMoving)
                {
                    posOffset.y = Mathf.Abs(Mathf.Sin(t)) * 0.065f;
                    rotOffsetDeg.z = Mathf.Sin(t) * 2.8f;
                    rotOffsetDeg.x = 3.0f;
                }
                else
                {
                    float breath = Mathf.Sin(t) * 0.018f;
                    scaleMult.y = 1.0f + breath;
                    posOffset.y = breath * 0.25f;
                    rotOffsetDeg.z = Mathf.Sin(t * 0.65f) * 1.1f;
                }
            }
            else if (enableStaticWindSway && !string.Equals(category, "Building", StringComparison.OrdinalIgnoreCase))
            {
                float t = time * 1.45f + _phase;
                rotOffsetDeg.z = Mathf.Sin(t) * 1.5f;
                rotOffsetDeg.x = Mathf.Cos(t * 0.72f) * 0.8f;
                posOffset.y = Mathf.Sin(t * 1.3f) * 0.008f;
            }

            if (_attackTimer > 0f)
            {
                _attackTimer = Mathf.Max(0f, _attackTimer - dt);
                float progress = 1f - (_attackTimer / Mathf.Max(0.01f, _attackDuration));
                float pulse = Mathf.Sin(progress * Mathf.PI);

                posOffset.z += pulse * 0.24f;
                posOffset.y += pulse * 0.08f;
                rotOffsetDeg.x += pulse * 10.0f;
                scaleMult += new Vector3(0.05f, 0.07f, 0.05f) * pulse;

                if (_attackTimer <= 0f && string.Equals(currentState, StateAttack, StringComparison.Ordinal))
                {
                    currentState = StateIdle;
                }
            }

            visualRoot.localPosition = _baseLocalPosition + posOffset;
            visualRoot.localRotation = _baseLocalRotation * Quaternion.Euler(rotOffsetDeg);
            visualRoot.localScale = Vector3.Scale(_baseLocalScale, scaleMult);
        }

        private void ResolveReferences()
        {
            if (visualRoot == null)
            {
                visualRoot = transform.childCount > 0 ? transform.GetChild(0) : transform;
            }

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>(true);
            }

            if (legacyAnimation == null)
            {
                legacyAnimation = GetComponentInChildren<Animation>(true);
            }

            FoundryAssetMetadata meta = GetComponent<FoundryAssetMetadata>();
            if (meta != null)
            {
                if (!string.IsNullOrEmpty(meta.rigProfile))
                {
                    rigProfile = meta.rigProfile;
                }

                if (!string.IsNullOrEmpty(meta.category))
                {
                    category = meta.category;
                }
            }

            if (_tailBone == null && visualRoot != null)
            {
                _tailBone = FindChildContaining(visualRoot, "tail");
            }
        }

        private void CacheBasePose()
        {
            if (visualRoot == null || _hasCachedBase)
            {
                return;
            }

            _baseLocalPosition = visualRoot.localPosition;
            _baseLocalRotation = visualRoot.localRotation;
            _baseLocalScale = visualRoot.localScale;
            _hasCachedBase = true;
        }

        private void RestoreBasePose()
        {
            if (!_hasCachedBase || visualRoot == null)
            {
                return;
            }

            visualRoot.localPosition = _baseLocalPosition;
            visualRoot.localRotation = _baseLocalRotation;
            visualRoot.localScale = _baseLocalScale;
        }

        private void ApplyClipState(string normalizedState)
        {
            if (animator != null && animator.runtimeAnimatorController != null && animator.isInitialized)
            {
                int stateHash = Animator.StringToHash(normalizedState);
                if (animator.HasState(0, stateHash))
                {
                    animator.CrossFadeInFixedTime(stateHash, 0.12f, 0);
                }

                TrySetAnimatorBool("IsMoving", string.Equals(normalizedState, StateMove, StringComparison.Ordinal));
            }

            if (legacyAnimation != null && legacyAnimation.GetClip(normalizedState) != null)
            {
                legacyAnimation.CrossFade(normalizedState, 0.12f);
            }
        }

        private void TrySetAnimatorBool(string paramName, bool value)
        {
            if (animator == null)
            {
                return;
            }

            AnimatorControllerParameter[] parameters = animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].type == AnimatorControllerParameterType.Bool
                    && string.Equals(parameters[i].name, paramName, StringComparison.Ordinal))
                {
                    animator.SetBool(paramName, value);
                    return;
                }
            }
        }

        private void TryTriggerAnimator(string paramName)
        {
            if (animator == null)
            {
                return;
            }

            AnimatorControllerParameter[] parameters = animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].type == AnimatorControllerParameterType.Trigger
                    && string.Equals(parameters[i].name, paramName, StringComparison.Ordinal))
                {
                    animator.SetTrigger(paramName);
                    return;
                }
            }
        }

        private void ComputeDeterministicPhase()
        {
            FoundryAssetMetadata meta = GetComponent<FoundryAssetMetadata>();
            string seed = meta != null && !string.IsNullOrEmpty(meta.assetId)
                ? meta.assetId
                : gameObject.name;

            int hash = 17;
            if (!string.IsNullOrEmpty(seed))
            {
                for (int i = 0; i < seed.Length; i++)
                {
                    hash = unchecked(hash * 31 + seed[i]);
                }
            }

            _phase = (hash & 0xFFFF) * (Mathf.PI * 2f / 65536f);
        }

        private static string NormalizeState(string state)
        {
            if (string.IsNullOrWhiteSpace(state))
            {
                return StateIdle;
            }

            string trimmed = state.Trim();
            if (string.Equals(trimmed, StateMove, StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "Walk", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "Run", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "Swim", StringComparison.OrdinalIgnoreCase))
            {
                return StateMove;
            }

            if (string.Equals(trimmed, StateAttack, StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "Strike", StringComparison.OrdinalIgnoreCase))
            {
                return StateAttack;
            }

            return StateIdle;
        }

        private static Transform FindChildContaining(Transform root, string needle)
        {
            if (root == null)
            {
                return null;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return child;
                }

                Transform nested = FindChildContaining(child, needle);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }
    }
}
