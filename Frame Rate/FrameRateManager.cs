﻿using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PWR.LowPowerMemoryConsumption {

	[DisallowMultipleComponent]
	public class FrameRateManager : MonoBehaviour {

		#region <<---------- Singleton ---------->>

        private static FrameRateManager _instance = null;

		/// <summary>
		/// Singleton instance.
		/// </summary>
        public static FrameRateManager Instance {
            get {
				if (_instance == null) {
					_instance = GameObject.FindObjectOfType<FrameRateManager>();
					if (_instance == null) {
						var goInstance = new GameObject();
						_instance = goInstance.AddComponent<FrameRateManager>();
					}
				}
                return _instance;
            }
        }

        #endregion <<---------- Singleton ---------->>




		#region <<---------- Properties and Fields ---------->>

		[SerializeField][Range(MinNumberOfSamples,60)] private int _smoothFramesCount = 5;

		[SerializeField] private int _fallbackFrameRate = 30;

		[SerializeField] private int _fallbackFixedFrameRate = 50;

		/// <summary>
		/// Number of frame rate samples to calculate <see cref="CurrentFrameRate"/>.
		/// </summary>
		public int SmoothFramesCount {
			get { return this._smoothFramesCount; }
			set {
				if (value <= 0) throw new ArgumentOutOfRangeException("smoothFramesCount", value, "must be greather than zero");
				this._smoothFramesCount = value;
			}
		}

		/// <summary>
		/// Target frame rate to set if there are no requests active.
		/// </summary>
		public int FallbackFrameRate {
			get { return this._fallbackFrameRate; }
			set {
				if (this._fallbackFrameRate == value) return;
				this._fallbackFrameRate = value;
				this.RecalculateTargetsRateIfPlaying();
			}
		}

		/// <summary>
		/// Target fixed frame rate to set if there are no requests active.
		/// </summary>
		public int FallbackFixedFrameRate {
			get { return this._fallbackFixedFrameRate; }
			set {
				if (this._fallbackFixedFrameRate == value) return;
				this._fallbackFixedFrameRate = value;
				this.RecalculateTargetsRateIfPlaying();
			}
		}

		/// <summary>
		/// Action invoked when <see cref="CurrentFrameRate"/> changes.
		/// </summary>
		public Action<int> onFrameRate;

		/// <summary>
		/// Action invoked when <see cref="CurrentFixedFrameRate"/> changes.
		/// </summary>
		public Action<int> onFixedFrameRate;

		/// <summary>
		/// Action invoked when <see cref="TargetFrameRate"/> changes.
		/// </summary>
		public Action<int> onTargetFrameRate;

		/// <summary>
		/// Action invoked when <see cref="TargetFixedFrameRate"/> changes.
		/// </summary>
		public Action<int> onTargetFixedFrameRate;

		private List<int> _samplesFrameRate = new List<int>();

		private int _currentFrameRate = 0;

		/// <summary>
		/// Current frames per second.
		/// </summary>
		public int CurrentFrameRate {
			get { return this._currentFrameRate; }
			private set {
				if (this._currentFrameRate == value) return;
				this._currentFrameRate = value;
				this.OnCurrentFrameRateChanged();
			}
		}

		private int _currentFixedFrameRate = 0;

		/// <summary>
		/// Current fixed frames per second
		/// </summary>
		public int CurrentFixedFrameRate {
			get { return this._currentFixedFrameRate; }
			private set {
				if (this._currentFixedFrameRate == value) return;
				this._currentFixedFrameRate = value;
				this.OnCurrentFixedFrameRateChanged();
			}
		}

		private int _targetFrameRate = 0;

		/// <summary>
		/// Target frames per second.
		/// </summary>
		public int TargetFrameRate {
			get { return this._targetFrameRate; }
			private set {
				if (this._targetFrameRate == value) return;
				this._targetFrameRate = value;
				this.OnTargetFrameRateChanged();
			}
		}

		private int _targetFixedFrameRate = 0;

		/// <summary>
		/// Target fixed frames per second.
		/// </summary>
		public int TargetFixedFrameRate {
			get { return this._targetFixedFrameRate; }
			private set {
				if (this._targetFixedFrameRate == value) return;
				this._targetFixedFrameRate = value;
				this.OnTargetFixedFrameRateChanged();
			}
		}

		/// <summary>
		/// TargetFrameRate can only be changed if VSync is off.
		/// </summary>
		/// <value>Returns true if <see cref="QualitySettings.vSyncCount"/> is zero or less. Otherwise false.</value>
		public static bool IsSupported {
			get {
				#if UNITY_EDITOR
				return QualitySettings.vSyncCount <= 0;
				#elif UNITY_IOS
				//on iOS VSync is always on, but Application.targetFrameRate seems to work.
				return true;
				#else
				return QualitySettings.vSyncCount <= 0;
				#endif
			}
		}

		private List<FrameRateRequest> _requests;

		public const string NotSupportedMessage = "TargetFrameRate can only be changed if VSync is off.";
		private const string DefaultName = "Frame Rate Manager";
		private const int MinNumberOfSamples = 1;

		#endregion <<---------- Properties and Fields ---------->>




		#region <<---------- MonoBehaviour ---------->>

        private void Awake() {

			if (_instance == null) {
				_instance = this;
			}
			else if (_instance != this) {

				if (Debug.isDebugBuild) Debug.LogWarning("[" + typeof(FrameRateManager).Name + "] trying to create another instance, destroying it", this);

				#if UNITY_EDITOR
				if (!Application.isPlaying) {
					DestroyImmediate(this);
					return;
				}
				#endif
				Destroy(this);
				return;
			}
			
			this.name = DefaultName;
			this.transform.SetParent(null, false);
			DontDestroyOnLoad(this);

			if (!FrameRateManager.IsSupported) Debug.LogWarning("[" + typeof(FrameRateManager).Name + "] " + FrameRateManager.NotSupportedMessage, this);
			this.RecalculateTargetsRateIfPlaying();
        }

		private void Update() {

			//calculate current frame rate
			this._samplesFrameRate.Add(Mathf.RoundToInt(1.0f / Time.unscaledDeltaTime));
			int maxSamples = Mathf.Max(MinNumberOfSamples, this._smoothFramesCount);
			while(this._samplesFrameRate.Count > maxSamples) this._samplesFrameRate.RemoveAt(0);
			float sum = 0;
			for (int i = 0; i < this._samplesFrameRate.Count; i++) sum += this._samplesFrameRate[i];
			this.CurrentFrameRate = Mathf.RoundToInt( sum / (float)this._samplesFrameRate.Count );

			//check if application target frame rate has changed from elsewhere
			if (Application.targetFrameRate != this._targetFrameRate) {
				this.SetApplicationTargetFrameRate(FrameRateType.FPS, this._targetFrameRate);
			}
		}

		private void FixedUpdate() {
			
			//calculate fixed frame rate
			this.CurrentFixedFrameRate = Mathf.RoundToInt(1.0f / Time.fixedUnscaledDeltaTime);

			//check if application fixed frame rate has changed from elsewhere
			if (this._currentFixedFrameRate != this._targetFixedFrameRate) {
				this.SetApplicationTargetFrameRate(FrameRateType.FixedFPS, this._targetFixedFrameRate);
			}
		}

		private void OnDestroy() {
			if (_instance != this) return;
			_instance = null;
		}

		#if UNITY_EDITOR
		private void OnValidate() {
			this.name = DefaultName;
			this.RecalculateTargetsRateIfPlaying();
		}
		private void Reset() {
			this.name = DefaultName;
			this.RecalculateTargetsRateIfPlaying();
		}
		#endif

        #endregion <<---------- MonoBehaviour ---------->>




		#region <<---------- Internal Callbacks ---------->>

		private void OnCurrentFrameRateChanged() {
			if (this.onFrameRate != null) this.onFrameRate(this._currentFrameRate);
		}

		private void OnCurrentFixedFrameRateChanged() {
			if (this.onFixedFrameRate != null) this.onFixedFrameRate(this._currentFixedFrameRate);
		}

		private void OnTargetFrameRateChanged() {
			this.SetApplicationTargetFrameRate(FrameRateType.FPS, this._targetFrameRate);
			if (this.onTargetFrameRate != null) this.onTargetFrameRate(this._targetFrameRate);
		}

		private void OnTargetFixedFrameRateChanged() {
			this.SetApplicationTargetFrameRate(FrameRateType.FixedFPS, this._targetFixedFrameRate);
			if (this.onTargetFixedFrameRate != null) this.onTargetFixedFrameRate(this._targetFixedFrameRate);
		}

		#endregion <<---------- Internal Callbacks ---------->>




		#region <<---------- Requests Management ---------->>

		/// <summary>
		/// Check if a request is added and active.
		/// </summary>
		/// <param name="request">Request to check.</param>
		/// <returns></returns>
		public bool ContainsRequest(FrameRateRequest request) {
			return request != null && this._requests != null && this._requests.Contains(request);
		}

		/// <summary>
		/// Add and activate a new frame rate request.
		/// </summary>
		/// <param name="request">Request to add.</param>
		public void AddRequest(FrameRateRequest request) {
			if (request == null || this.ContainsRequest(request)) return;
			if (this._requests == null) this._requests = new List<FrameRateRequest>();
			this._requests.Add(request);
			request.onRequestChanged += this.OnRequestChangedCallback;
			this.RecalculateTargetsRateIfPlaying();
		}

		/// <summary>
		/// Remove and deactivate a frame rate request.
		/// </summary>
		/// <param name="request">Request to remove.</param>
		public void RemoveRequest(FrameRateRequest request) {
			if (request == null || !this.ContainsRequest(request)) return;
			this._requests.Remove(request);
			request.onRequestChanged -= this.OnRequestChangedCallback;
			this.RecalculateTargetsRateIfPlaying();
		}

		private void OnRequestChangedCallback(FrameRateRequest request) {
			if (request == null) return;
			if (!this.ContainsRequest(request)) {
				request.onRequestChanged -= this.OnRequestChangedCallback;
				return;
			}
			this.RecalculateTargetsRateIfPlaying();
		}

		#endregion <<---------- Requests Management ---------->>




		#region <<---------- General ---------->>

		private void SetApplicationTargetFrameRate(FrameRateType type, int value) {
			switch (type) {

				case FrameRateType.FPS:
					Application.targetFrameRate = value;
				break;

				case FrameRateType.FixedFPS:
					Time.fixedDeltaTime = 1.0f / (float)value;
				break;
			}
		}

		private void RecalculateTargetsRateIfPlaying() {
			#if UNITY_EDITOR
			if (!Application.isPlaying) return;
			#endif

			int newTarget = FrameRateRequest.MinValueForType(FrameRateType.FPS) - 1;
			int newTargetFixed = FrameRateRequest.MinValueForType(FrameRateType.FixedFPS) - 1;

			if (this._requests != null && this._requests.Count > 0) {
				for (int i = this._requests.Count - 1; i >= 0; i--) {
					if (this._requests[i] == null) {
						this._requests.RemoveAt(i);
						continue;
					}
					if (!this._requests[i].IsValid) continue;

					switch (this._requests[i].Type) {
						case FrameRateType.FPS:
							newTarget = Mathf.Max(newTarget, this._requests[i].Value);
						break;

						case FrameRateType.FixedFPS:
							newTargetFixed = Mathf.Max(newTargetFixed, this._requests[i].Value);
						break;
					}
				}
			}

			if (newTarget < FrameRateRequest.MinValueForType(FrameRateType.FPS)) {
				newTarget = this._fallbackFrameRate;
			}
			if (newTargetFixed < FrameRateRequest.MinValueForType(FrameRateType.FixedFPS)) {
				newTargetFixed = this._fallbackFixedFrameRate;
			}

			this.TargetFrameRate = newTarget;
			this.TargetFixedFrameRate = newTargetFixed;
		}

		#endregion <<---------- General ---------->>




		#region <<---------- Custom Inspector ---------->>
		#if UNITY_EDITOR
		[CustomEditor(typeof(FrameRateManager))]
		private class CustomInspector : Editor {

			FrameRateManager script;

			void OnEnable() {
				script = this.target as FrameRateManager;
			}

			public override void OnInspectorGUI() {
				this.serializedObject.Update();
				this.DrawDefaultInspector();

				if (!FrameRateManager.IsSupported) {
					EditorGUILayout.HelpBox(FrameRateManager.NotSupportedMessage, MessageType.Warning);
				}

				int minRateValue = FrameRateRequest.MinValueForType(FrameRateType.FPS);
				int minFixedRateValue = FrameRateRequest.MinValueForType(FrameRateType.FixedFPS);

				if (script._fallbackFrameRate < minRateValue) {
					EditorGUILayout.HelpBox("Minimum value for " + FrameRateType.FPS.ToString() + " is " + minRateValue, MessageType.Warning);
					EditorGUILayout.Space();
				}

				if (script._fallbackFixedFrameRate < minFixedRateValue) {
					EditorGUILayout.HelpBox("Minimum value for " + FrameRateType.FixedFPS.ToString() + " is " + minFixedRateValue, MessageType.Warning);
					EditorGUILayout.Space();
				}
			}
		}
		#endif
		#endregion <<---------- Custom Inspector ---------->>
	}
}