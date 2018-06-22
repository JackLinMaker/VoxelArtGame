#if UNITY_EDITOR
#define INPUT_MODE  //需要编辑触摸代码时注释此行，否则是编辑鼠标代码
#endif

using UnityEngine;
using System;
using UnityEngine.EventSystems;
using System.Collections.Generic;

public interface TrackballListener
{
	void OnTrackballTouchStart();
	void OnTrackballTouchEnd();
	void OnTrackballSwipe(Vector3 pos);
	bool OnTrackballClick(Vector3 pos);
	void OnTrackballLongClick(Vector3 Pos);
	void OnTrackballTranslate(Vector3 position);
	void OnTrackballRotate(float pitch, float yaw);
	void OnTrackballScale(float scale);
	void OnTrackballTransform(Trackball trackball);
}

public class FingerTrack : MonoBehaviour
{
	public float m_LongClickSeconds = 0.35f;

	public TrackballListener Listener { get; set; }
	public float ScreenToWorld { get; set; }
	public float ScreenDensity { get; set; }
	public float TouchSlop { get; set; }
	public float Pitch { get { return mPitch; } }
	public float Yaw { get { return mYaw; } }
	public float Scale { get { return mScale; } }
	public float MaxScale { get; set; }
	public bool DisableListeningSwipe { get; set; }

	public const float MaxPitch = 85;
	//这个仰角可以让(-1, 1, -1)向量投影成z轴，可以利用来实现空间错觉
	//const float DefaultPitch = -35.26439f;
	public const float DefaultPitch = -15f;
	public const float DefaultYaw = -45;
	//X轴旋转角度，即仰角
	float mPitch;
	//Y轴旋转角度
	float mYaw;
	float mScale = 1;
	Transform mTargetTransform;

	bool mTouchDownOnUI;
	bool mTouchMoved;
	bool mPreparingClick;
	float mPreparingClickStartTime;

#if INPUT_MODE
	bool mMouseTouching = false;
	Vector3 mMouseDownPos;
	Vector3 mLastMousePos;

	public bool IsTouching { get { return mMouseTouching; } }
#else
	const int PINCH_RESET = 0;
	const int PINCH_ZOOM = 1;
	const int PINCH_MOVE = 2;

	int mTouchCount;
	Vector2 mLastTouchPos0;
	Vector2 mLastTouchPos1;
	int mPinchMode;
	const int PinchNum = 32;
	const float PinchExpiredTime = 0.2f;
	Vector2[] mPinchPos0 = new Vector2[PinchNum];
	Vector2[] mPinchPos1 = new Vector2[PinchNum];
	float[] mPinchTime = new float[PinchNum];
	int mPinchLast = 0;

	public bool IsTouching { get { return mTouchCount > 0; } }
#endif

	Damping mDampingX = new Damping(0, Damping.FastFriction);
	Damping mDampingY = new Damping(0, Damping.FastFriction);
	Damping mDampingPitch = new Damping();
	Damping mDampingYaw = new Damping();
	Damping mDampingScale = new Damping();
	List<Damping> mTransformDampings;
	bool mInMotion;

	void Awake()
	{
		mTransformDampings = new List<Damping>();
		mTransformDampings.Add(mDampingX);
		mTransformDampings.Add(mDampingY);
		mTransformDampings.Add(mDampingPitch);
		mTransformDampings.Add(mDampingYaw);
		mTransformDampings.Add(mDampingScale);
	}

	public void Reset(Transform targetTransform)
	{
		mTargetTransform = targetTransform;
		if (mTargetTransform == null) {
			mTargetTransform = transform;
		}
		ScreenToWorld = 1;
		MaxScale = 5;
		foreach (Damping damping in mTransformDampings) {
			damping.Stop();
		}
		StopMotion();

		SetScale(1);
		SetRotation(DefaultPitch, DefaultYaw);
		SetPosition(Vector3.zero);
		SaveTransformValues();
	}

	public void ResetTransfrom(ModelInfo info) {
		SetScale(1);
		if (info == null) {
			SetRotation(DefaultPitch, DefaultYaw);
			SetPosition(Vector3.zero);
		} else {
			SetRotation(info.m_Pitch, info.m_Yaw);
			SetPosition(new Vector3(info.m_PosX, info.m_PosY, 0));
		}
		SaveTransformValues();
	}

	public void OffsetYaw(float deltaYaw) {
		SetRotation(mPitch, mYaw + deltaYaw);
	}

	public void MotionTo(Vector3 targetPos, float pitch, float yaw, float scale, float overshoot, float friction) {
		pitch = mPitch + MathUtil.ReduceAngle(pitch - mPitch);
		yaw = mYaw + MathUtil.ReduceAngle(yaw - mYaw);
		//Debug.Log("MotionTo: " + mPitch + " -> " + pitch + "  yaw:" + mYaw + " -> " + yaw);
		//Debug.Log("curPitch=" + mDampingPitch.Value + " curYaw=" + mDampingYaw.Value);
		mDampingX.Target = targetPos.x;
		mDampingY.Target = targetPos.y;
		mDampingPitch.Target = pitch;
		//注意mDampingYaw的当前value可能是超过360度，而mYaw的值是规约后的，重置一下避免多转一圈的情况
		mDampingYaw.Reset(mYaw, mDampingYaw.Velocity, yaw);
		mDampingScale.Target = scale;

		foreach (Damping damping in mTransformDampings) {
			damping.SetFriction(overshoot, friction);
		}
		mInMotion = true;
	}

	void StopMotion() {
		if (mInMotion) {
			mInMotion = false;
			foreach (Damping damping in mTransformDampings) {
				damping.RevertToInitFriction();
			}
		}
	}

	void SaveTransformValues() {
		Vector3 localPosition = mTargetTransform.localPosition;
		mDampingX.Reset(localPosition.x, 0, localPosition.x);
		mDampingY.Reset(localPosition.y, 0, localPosition.y);
		mDampingPitch.Reset(mPitch, 0, mPitch);
		mDampingYaw.Reset(mYaw, 0, mYaw);
		mDampingScale.Reset(mScale, 0, mScale);
	}

	void Update()
	{
		if (mTargetTransform == null || !mTargetTransform.gameObject.activeSelf) {
			return;
		}
#if INPUT_MODE
		if (Input.GetMouseButtonDown(0)) {
			mTouchDownOnUI = EventSystem.current.IsPointerOverGameObject();
		}
		if (!mTouchDownOnUI) {
			CheckMouse();
		}
#else
		if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began ) {
			mTouchDownOnUI = EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId);
		}
		if (!mTouchDownOnUI) {
			CheckTouch();
		}
#endif

		Animate(Time.deltaTime);
	}

	void SetPosition(Vector3 pos) {
		pos.z = 0;
		mTargetTransform.localPosition = pos;

		if (Listener != null) {
			Listener.OnTrackballTranslate(pos);
		}
	}

	public static Quaternion CreateRotation(float pitch, float yaw) {
		//直接使用(pitch, yaw, 0)的欧拉角的顺序是先yaw，后pitch的，不符合这里的需求
		Quaternion q1 = Quaternion.Euler(new Vector3(pitch, 0, 0));
		Quaternion q2 = Quaternion.Euler(new Vector3(0, yaw, 0));
		return q1 * q2;
	}

	public static Quaternion CreateInverseRotation(float pitch, float yaw)
	{
		return Quaternion.Euler(new Vector3(-pitch, -yaw, 0));
	}

	void SetRotation(float pitch, float yaw)
	{
		mPitch = Mathf.Clamp(pitch, -MaxPitch, MaxPitch);
		mYaw = yaw % 360;
		mTargetTransform.localRotation = CreateRotation(mPitch, mYaw);

		if (Listener != null) {
			Listener.OnTrackballRotate(mPitch, mYaw);
		}

	}

	void SetScale(float scale)
	{
		Vector3 p = mTargetTransform.localPosition;
		float s1 = mTargetTransform.localScale.x;
		Quaternion q = mTargetTransform.localRotation;

		float s2 = mScale = Mathf.Clamp(scale, 1, MaxScale);
		mTargetTransform.localScale = Vector3.one * mScale;

		//已知S * s1 = s2，以及S * p * q * s1 = p' * q * s2，求p'。
		//将S = s2 / s1代入式2，得(s2/s1) * p * q * s1 = p' * q * s2，
		//则p' = (s2/s1) * p * q * (s1/s2) * q^-1 = (s2/s1) * {p * q * (s1/s2)} * {q^-1}

		Matrix4x4 a = new Matrix4x4();
		a.SetTRS(p, q, Vector3.one * (s1 / s2));
		Matrix4x4 b = new Matrix4x4();
		b.SetTRS(Vector3.zero, Quaternion.Inverse(q), Vector3.one);
		a *= b;
		mTargetTransform.localPosition = new Vector3(a.m03, a.m13, 0) * (s2 / s1);

		if (Listener != null) {
			Listener.OnTrackballScale(mScale);
		}

	}


#if INPUT_MODE
	void CheckMouse()
	{
		if (Input.GetMouseButtonDown(0)) {
			StopMotion();
			mTouchMoved = false;
			mMouseTouching = true;
			mLastMousePos = mMouseDownPos = Input.mousePosition;
			mPreparingClick = true;
			mPreparingClickStartTime = Time.realtimeSinceStartup;
			if (Listener != null) {
				Listener.OnTrackballTouchStart();
			}
		} else if (Input.GetMouseButtonUp(0)) {
			mMouseTouching = false;
			CheckClick(mLastMousePos);
			if (Listener != null) {
				Listener.OnTrackballTouchEnd();
			}
		} else if (mMouseTouching) {
			Vector3 pos = Input.mousePosition;
			bool touchMoved = mTouchMoved;
			mTouchMoved = Vector3.Distance(pos, mMouseDownPos) > TouchSlop;
			if (!touchMoved && mTouchMoved) {
				mPreparingClick = false;
				mLastMousePos = pos;
				SaveTransformValues();
			}
			if (mTouchMoved) {
				if (!DisableListeningSwipe) {
					if (Input.GetKey("a")) {
						//Vector2 delta = (pos - mLastMousePos) * screenToWorld;
						//OffsetPosition(delta.x, delta.y);
						mDampingX.Target += (pos.x - mLastMousePos.x) * ScreenToWorld;
						mDampingY.Target += (pos.y - mLastMousePos.y) * ScreenToWorld;
					} else if (Input.GetKey("s")) {
						//mScale += (pos.y - mLastMousePos.y) / Screen.height * MaxScale;
						//SetScale(mScale);
						float scale = mDampingScale.Target;
						scale += (pos.y - mLastMousePos.y) / Screen.height * MaxScale * 1.5f;
						scale = Mathf.Clamp(scale, 1, MaxScale);
						mDampingScale.Target = scale;
					} else {
						//mYaw -= (pos.x - mLastMousePos.x) / Screen.width * 180 * 1.5f;
						//mPitch += (pos.y - mLastMousePos.y) / Screen.height * 2.5f * MaxPitch;
						//SetRotation(mPitch, mYaw);
						mDampingYaw.Target -= (pos.x - mLastMousePos.x) / Screen.width * 180 * 1.5f;
						mDampingPitch.Target += (pos.y - mLastMousePos.y) / Screen.height * 2.5f * MaxPitch;
					}
				} else {
					if (Listener != null) {
						Listener.OnTrackballSwipe(pos);
					}
				}
				mLastMousePos = pos;
			}
		}

		if (mPreparingClick && Time.realtimeSinceStartup - mPreparingClickStartTime > m_LongClickSeconds) {
			mPreparingClick = false;
			if (Listener != null) {
				Listener.OnTrackballLongClick(mLastMousePos);
			}
		}
	}
#endif

#if !INPUT_MODE    //不放到#else分支是为了避免编辑器格式化错乱问题，需要编辑代码时注释这句宏判断
	void CheckTouch() {
		int touchCount = Input.touchCount;
		if (mTouchCount != touchCount) {
			if (mTouchCount == 0 && touchCount > 0) {
				StopMotion();
				if (Listener != null) {
					Listener.OnTrackballTouchStart();
				}
			} else if (mTouchCount > 0 && touchCount == 0) {
				if (Listener != null) {
					Listener.OnTrackballTouchEnd();
				}
			}
			mTouchCount = touchCount;
			mPinchMode = PINCH_RESET;
			for (int i = 0; i < PinchNum; ++i) {
				mPinchTime[i] = 0;
			}
			SaveTouch();
		}

		if (touchCount > 0) {
			Touch touch0 = Input.GetTouch(0);
			if (touchCount == 1) {
				Vector2 pos = touch0.position;
				switch (touch0.phase) {
					case TouchPhase.Began:
						{
							mTouchMoved = false;
							mPreparingClick = true;
							mPreparingClickStartTime = Time.realtimeSinceStartup;
							mLastTouchPos0 = pos;
						}
						break;
					case TouchPhase.Moved:
						{
							mTouchMoved |= Moved(pos, mLastTouchPos0);
							if (mTouchMoved) {
								if (mPreparingClick) {
									mPreparingClick = false;
									mLastTouchPos0 = pos;
								}
								if (!DisableListeningSwipe) {
									//mYaw -= (pos.x - mLastTouchPos0.x) / Screen.width * 180 * 1.5f;
									//mPitch += (pos.y - mLastTouchPos0.y) / Screen.height * 2.5f * MaxPitch;
									//SetRotation(mPitch, mYaw);
									mDampingYaw.Target -= (pos.x - mLastTouchPos0.x) / (200 * ScreenDensity) * 180;
									float deltaPitch = (pos.y - mLastTouchPos0.y) / (300 * ScreenDensity) * 180;
									mDampingPitch.Target = Mathf.Clamp(mDampingPitch.Target + deltaPitch, -MaxPitch, MaxPitch);
								} else {
									if (Listener != null) {
										Listener.OnTrackballSwipe(pos);
									}
								}
								mLastTouchPos0 = pos;
							}
						}
						break;
					case TouchPhase.Canceled:
						{
							mPreparingClick = false;
						}
						break;
					case TouchPhase.Ended:
						{
							CheckClick(pos);
						}
						break;
				}
				if (mPreparingClick && Time.realtimeSinceStartup - mPreparingClickStartTime > m_LongClickSeconds) {
					mPreparingClick = false;
					if (Listener != null) {
						Listener.OnTrackballLongClick(pos);
					}
				}
			} else if (touchCount == 2) {
				Touch touch1 = Input.GetTouch(1);
				if (touch0.phase == TouchPhase.Moved || touch1.phase == TouchPhase.Moved) {
					Vector2 pos0 = touch0.position;
					Vector2 pos1 = touch1.position;
					float pinchTime = Time.realtimeSinceStartup;
					int pinchMode = mPinchMode;
					float acceptableTime = pinchTime - PinchExpiredTime;
						if (mPinchMode == PINCH_RESET) {
							acceptableTime = 0;
						}
					for (int i = mPinchLast; mPinchTime[i] >= acceptableTime; ) {
						Vector4 oldPos0 = mPinchPos0[i];
						Vector4 oldPos1 = mPinchPos1[i];
						if (Moved(oldPos0, pos0) && Moved(oldPos1, pos1)) {
							pinchMode = PinchMode(oldPos0, oldPos1, pos0, pos1);
							break;
						}
						int prev = (i - 1 + PinchNum) % PinchNum;
						if (prev == mPinchLast) {
							break;
						}
						i = prev;
					}
					if (mPinchMode != pinchMode && pinchMode != PINCH_RESET) {
						mPinchMode = pinchMode;
						SaveTouch();
					}
					switch (mPinchMode) {
						case PINCH_RESET:
							break;
						case PINCH_ZOOM: {
								float delta = Vector2.Distance(pos0, pos1) - Vector2.Distance(mLastTouchPos0, mLastTouchPos1);
								float deltaScale = delta / ScreenDensity / 300 * MaxScale;
								float target = mDampingScale.Target + deltaScale;
								mDampingScale.Target = Mathf.Clamp(target, 1, MaxScale);
							}
							break;
						case PINCH_MOVE: {
								Vector2 delta = (pos0 + pos1 - mLastTouchPos0 - mLastTouchPos1) / 2 * ScreenToWorld;
								mDampingX.Target += delta.x;
								mDampingY.Target += delta.y;
							}
							break;
					}
					mLastTouchPos0 = pos0;
					mLastTouchPos1 = pos1;
					//Add pinch movement
					if (++mPinchLast >= PinchNum) {
						mPinchLast = 0;
					}
					mPinchPos0[mPinchLast] = pos0;
					mPinchPos1[mPinchLast] = pos1;
					mPinchTime[mPinchLast] = pinchTime;
				}
			}
		}

	}

	bool Moved(Vector2 a, Vector2 b) {
		a -= b;
		return Mathf.Abs(a.x) > TouchSlop || Mathf.Abs(a.y) > TouchSlop;
	}

	//双指p1, q1移动到p2, q2，检测是平移(双指移动方向相同)还是缩放(双指移动方向相反)
	int PinchMode(Vector2 p1, Vector2 q1, Vector2 p2, Vector2 q2) {
		Vector2 p = p2 - p1;
		Vector2 q = q2 - q1;
		float angle = Vector2.Angle(p, q);
		if (angle < 60) {
			return PINCH_MOVE;
		}
		if (angle > 120) {
			return PINCH_ZOOM;
		}
		return PINCH_RESET;
	}

	void SaveTouch() {
		if (Input.touchCount > 0) {
			mPreparingClick = false;
			mLastTouchPos0 = Input.GetTouch(0).position;
			if (Input.touchCount > 1) {
				mLastTouchPos1 = Input.GetTouch(1).position;
			}
			SaveTransformValues();
		}
	}
#endif

	void CheckClick(Vector3 pos)
	{
		if (mPreparingClick) {
			mPreparingClick = false;
			if (Listener == null || !Listener.OnTrackballClick(pos)) {
				mDampingX.Reset(mTargetTransform.localPosition.x, 0, 0);
				mDampingY.Reset(mTargetTransform.localPosition.y, 0, 0);
				mDampingScale.Reset(mScale, 0, MaxScale / 2);
			}
		}
	}

	void Animate(float dt)
	{
		bool finished = true;
		foreach (Damping damping in mTransformDampings) {
			damping.Next(dt);
			finished &= damping.Finished;
		}
		if (finished) {
			StopMotion();
			return;
		}

		if (!mDampingScale.Finished) {
			SetScale(mDampingScale.Value);
		}

		if (!mDampingPitch.Finished || !mDampingYaw.Finished) {
			SetRotation(mDampingPitch.Value, mDampingYaw.Value);
		}

		if (mInMotion || !mDampingX.Finished || !mDampingY.Finished) {
			SetPosition(new Vector3(mDampingX.Value, mDampingY.Value, 0));
		}

		if (Listener != null) {
			Listener.OnTrackballTransform(this);
		}

	}

	public void Copy(Trackball trackball) {
		m_LongClickSeconds = trackball.m_LongClickSeconds;
		ScreenToWorld = trackball.ScreenToWorld;
		ScreenDensity = trackball.ScreenDensity;
		TouchSlop = trackball.TouchSlop;
		MaxScale = trackball.MaxScale;

	}
}
