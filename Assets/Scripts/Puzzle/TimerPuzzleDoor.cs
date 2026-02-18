using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

namespace CloneGame.Puzzle
{
    // Door that opens when all sensors are active, then closes after a timer.
    // Used in Puzzle 2 where you have to make it through before it closes.
    public class TimerPuzzleDoor : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private List<PuzzleSensor> _requiredSensors = new List<PuzzleSensor>();
        [SerializeField] private Transform _doorPanel;
        [SerializeField] private Renderer _indicatorRenderer;

        [Header("Door Settings")]
        [SerializeField] private Vector3 _openOffset = new Vector3(0f, 3f, 0f);
        [SerializeField] private float _openSpeed = 3f;
        [SerializeField] private float _closeSpeed = 1.5f;

        [Header("Timer Settings")]
        [SerializeField] private float _stayOpenDuration = 5f;
        [SerializeField] private bool _requireContinuousActivation = false;

        [Header("Visual Feedback")]
        [SerializeField] private Color _lockedColor = Color.red;
        [SerializeField] private Color _unlockedColor = Color.green;
        [SerializeField] private Color _timerColor = Color.yellow;

        [Header("Audio")]
        [SerializeField] private AudioClip _openSound;
        [SerializeField] private AudioClip _closeWarningSound;
        [SerializeField] private AudioClip _closeSound;

        [Header("Events")]
        public UnityEvent OnDoorOpened;
        public UnityEvent OnDoorClosing;
        public UnityEvent OnDoorClosed;
        public UnityEvent<float> OnTimerTick; // remaining time in seconds

        private Vector3 _closedPosition;
        private Vector3 _openPosition;
        private bool _isOpen = false;
        private bool _isUnlocked = false;
        private float _openTimer = 0f;
        private bool _timerActive = false;
        private MaterialPropertyBlock _propBlock;
        private AudioSource _audioSource;

        public bool IsOpen => _isOpen;
        public bool IsUnlocked => _isUnlocked;
        public float RemainingTime => _timerActive ? Mathf.Max(0f, _stayOpenDuration - _openTimer) : 0f;
        public float TimerProgress => _timerActive ? _openTimer / _stayOpenDuration : 0f;

        private void Awake()
        {
            _propBlock = new MaterialPropertyBlock();
            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null)
            {
                _audioSource = gameObject.AddComponent<AudioSource>();
                _audioSource.spatialBlend = 1f;
            }
        }

        private void Start()
        {
            if (_doorPanel != null)
            {
                _closedPosition = _doorPanel.localPosition;
                _openPosition = _closedPosition + _openOffset;
            }

            // auto-collect sensors if none were assigned
            if (_requiredSensors.Count == 0)
                _requiredSensors.AddRange(FindObjectsByType<PuzzleSensor>(FindObjectsSortMode.None));

            UpdateIndicator();
        }

        private void Update()
        {
            CheckSensors();
            UpdateTimer();
            UpdateDoorPosition();
        }

        private void CheckSensors()
        {
            bool allActive = true;
            int activeCount = 0;

            foreach (var sensor in _requiredSensors)
            {
                if (sensor != null)
                {
                    if (sensor.IsActive)
                        activeCount++;
                    else
                        allActive = false;
                }
            }

            if (allActive && !_isUnlocked)
            {
                _isUnlocked = true;
                _isOpen = true;
                _timerActive = true;
                _openTimer = 0f;

                Debug.Log($"[TimerDoor] UNLOCKED! Door open for {_stayOpenDuration} seconds!");
                PlaySound(_openSound);
                OnDoorOpened?.Invoke();
                UpdateIndicator();
            }
            else if (!allActive && _isUnlocked && _requireContinuousActivation)
            {
                _isUnlocked = false;
                _isOpen = false;
                _timerActive = false;

                Debug.Log("[TimerDoor] LOCKED - Sensors deactivated!");
                PlaySound(_closeSound);
                OnDoorClosed?.Invoke();
                UpdateIndicator();
            }
        }

        private void UpdateTimer()
        {
            if (!_timerActive) return;

            _openTimer += Time.deltaTime;
            float remaining = _stayOpenDuration - _openTimer;

            OnTimerTick?.Invoke(remaining);

            // 2 second warning beep
            if (remaining <= 2f && remaining > 1.9f)
            {
                PlaySound(_closeWarningSound);
                OnDoorClosing?.Invoke();
            }

            if (_openTimer >= _stayOpenDuration)
            {
                _timerActive = false;
                _isOpen = false;
                _isUnlocked = false;

                Debug.Log("[TimerDoor] Timer expired! Door closing!");
                PlaySound(_closeSound);
                OnDoorClosed?.Invoke();
                UpdateIndicator();
            }
            else
            {
                UpdateTimerIndicator(remaining);
            }
        }

        private void UpdateDoorPosition()
        {
            if (_doorPanel == null) return;

            Vector3 targetPos = _isOpen ? _openPosition : _closedPosition;
            float speed = _isOpen ? _openSpeed : _closeSpeed;

            _doorPanel.localPosition = Vector3.Lerp(
                _doorPanel.localPosition,
                targetPos,
                Time.deltaTime * speed
            );
        }

        private void UpdateIndicator()
        {
            if (_indicatorRenderer == null) return;

            _indicatorRenderer.GetPropertyBlock(_propBlock);
            Color color = _isUnlocked ? _unlockedColor : _lockedColor;
            _propBlock.SetColor("_BaseColor", color);
            _propBlock.SetColor("_EmissiveColor", color * 2f);
            _indicatorRenderer.SetPropertyBlock(_propBlock);
        }

        private void UpdateTimerIndicator(float remaining)
        {
            if (_indicatorRenderer == null) return;

            float t = remaining / _stayOpenDuration;
            Color color = Color.Lerp(_timerColor, _unlockedColor, t);

            // pulse red when about to close
            if (remaining <= 2f)
            {
                float pulse = Mathf.Sin(Time.time * 10f) * 0.5f + 0.5f;
                color = Color.Lerp(_lockedColor, _timerColor, pulse);
            }

            _indicatorRenderer.GetPropertyBlock(_propBlock);
            _propBlock.SetColor("_BaseColor", color);
            _propBlock.SetColor("_EmissiveColor", color * 2f);
            _indicatorRenderer.SetPropertyBlock(_propBlock);
        }

        private void PlaySound(AudioClip clip)
        {
            if (clip != null && _audioSource != null)
                _audioSource.PlayOneShot(clip);
        }

        public int GetActiveSensorCount()
        {
            int count = 0;
            foreach (var sensor in _requiredSensors)
            {
                if (sensor != null && sensor.IsActive) count++;
            }
            return count;
        }

        // useful for testing without having to trigger all sensors
        [ContextMenu("Force Open")]
        public void ForceOpen()
        {
            _isUnlocked = true;
            _isOpen = true;
            _timerActive = true;
            _openTimer = 0f;
            OnDoorOpened?.Invoke();
            UpdateIndicator();
        }

        [ContextMenu("Force Close")]
        public void ForceClose()
        {
            _isUnlocked = false;
            _isOpen = false;
            _timerActive = false;
            OnDoorClosed?.Invoke();
            UpdateIndicator();
        }
    }
}
