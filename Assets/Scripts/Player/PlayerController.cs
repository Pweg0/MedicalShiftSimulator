using UnityEngine.InputSystem;
using UnityEngine;

namespace MedicalShiftSimulator.Player
{
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [SerializeField] private float moveSpeed = 4f;
        [SerializeField] private float gravity = -9.81f;
        [SerializeField] private float rotationSpeed = 720f;

        private CharacterController _controller;
        private Vector3 _verticalVelocity;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
        }

private void Update()
        {
            var keyboard = Keyboard.current;
            float horizontal = 0f;
            float vertical = 0f;

            if (keyboard != null)
            {
                if (keyboard.aKey.isPressed) horizontal -= 1f;
                if (keyboard.dKey.isPressed) horizontal += 1f;
                if (keyboard.sKey.isPressed) vertical -= 1f;
                if (keyboard.wKey.isPressed) vertical += 1f;
            }

            Vector3 move = new Vector3(horizontal, 0f, vertical);
            if (move.sqrMagnitude > 1f)
            {
                move.Normalize();
            }

            if (move.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(move, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
            }

            if (_controller.isGrounded && _verticalVelocity.y < 0f)
            {
                _verticalVelocity.y = -0.5f;
            }
            _verticalVelocity.y += gravity * Time.deltaTime;

            Vector3 motion = move * moveSpeed + _verticalVelocity;
            _controller.Move(motion * Time.deltaTime);
        }
    }
}
