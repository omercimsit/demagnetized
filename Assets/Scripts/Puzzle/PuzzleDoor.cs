using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

namespace CloneGame.Puzzle
{
    // door that slides open when all connected sensors are satisfied
    public class PuzzleDoor : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private List<PuzzleSensor> _requiredSensors = new List<PuzzleSensor>();
        [SerializeField] private Transform _doorPanel;
        [SerializeField] private Renderer _indicatorRenderer;

        [Header("Door Settings")]
        [SerializeField] private Vector3 _openOffset = new Vector3(0f, 3f, 0f);
        [SerializeField] private float _openSpeed = 2f;

        [Header("Visual Feedback")]
        [SerializeField] private Color _lockedColor = Color.red;
        [SerializeField] private Color _unlockedColor = Color.green;

        [Header("Events")]
        public UnityEvent OnDoorOpened;
        public UnityEvent OnDoorClosed;

        private Vector3 _closedPosition;
        private Vector3 _openPosition;
        private bool _isOpen = false;
        private bool _isUnlocked = false;
        private MaterialPropertyBlock _propBlock;

        private void Awake()
        {
            _propBlock = new MaterialPropertyBlock();
        }

        private void Start()
        {
            if (_doorPanel != null)
            {
                _closedPosition = _doorPanel.localPosition;
                _openPosition = _closedPosition + _openOffset;
            }

            // auto-find sensors in scene if none were assigned in the inspector
            if (_requiredSensors.Count == 0)
            {
                _requiredSensors.AddRange(FindObjectsByType<PuzzleSensor>(FindObjectsSortMode.None));
            }

            UpdateIndicator();
        }

        private void Update()
        {
            CheckSensors();
            UpdateDoorPosition();
        }

        private void CheckSensors()
        {
            bool allActive = true;
            foreach (var sensor in _requiredSensors)
            {
                if (sensor != null && !sensor.IsActive)
                {
                    allActive = false;
                    break;
                }
            }

            if (allActive != _isUnlocked)
            {
                _isUnlocked = allActive;
                UpdateIndicator();

                if (_isUnlocked)
                {
                    Debug.Log("[Door] UNLOCKED - All sensors active!");
                    _isOpen = true;
                    OnDoorOpened?.Invoke();
                }
                else
                {
                    Debug.Log("[Door] LOCKED - Sensors deactivated");
                    _isOpen = false;
                    OnDoorClosed?.Invoke();
                }
            }
        }

        private void UpdateDoorPosition()
        {
            if (_doorPanel == null) return;

            Vector3 targetPos = _isOpen ? _openPosition : _closedPosition;
            _doorPanel.localPosition = Vector3.Lerp(
                _doorPanel.localPosition,
                targetPos,
                Time.deltaTime * _openSpeed
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

        public bool IsOpen => _isOpen;
        public bool IsUnlocked => _isUnlocked;

        // TODO: might want to expose this in the UI somewhere
        public int GetActiveSensorCount()
        {
            int count = 0;
            foreach (var sensor in _requiredSensors)
            {
                if (sensor != null && sensor.IsActive) count++;
            }
            return count;
        }
    }
}
