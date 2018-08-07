﻿using BeardedManStudios.Forge.Networking.Generated;
using System;
using System.Linq;
using UnityEngine;

namespace AuthMovementExample
{
    /*
     * The networked player object
     * 
     * Server-owned but we can find the local owner by comparing the NetworkIds
     * 
     * Clientside does prediction and replaying of inputs from latest server update (reconciliation)
     * Serverside does processing of inputs sent from clients and has authority over the player object's state
     */

    public class Player : PlayerBehavior
    {
        #region Inspector
        [Tooltip("The movement speed of the player.")]
        public float Speed = 1.0f;
        #endregion

        private Rigidbody _rigidBody;
     
        private bool _setup = false;
        private bool _isLocalOwner = false;

        private InputFrame _currentInput = null;
        private InputListener _inputListener;

        // Last frame that was processed locally on this machine
        private uint _lastLocalFrame = 0;
        // Last frame that was sent (server)/received (client) on the network
        private uint _lastNetworkFrame = 0;

        private void Awake()
        {
            _rigidBody = GetComponent<Rigidbody>();

            // DKE: Removed 2D collider related code          
        }

        private void Update()
        {
            // Set the networked fields in Update so we are
            // up to date per the last physics update
            if (networkObject.IsServer)
            {
                if (_lastNetworkFrame < _lastLocalFrame)
                {
                    _lastNetworkFrame = _lastLocalFrame;
                    networkObject.frame = _lastLocalFrame;
                }
                networkObject.position = _rigidBody.position;
                networkObject.rotation = _rigidBody.rotation;
            }
        }

        void FixedUpdate()
        {
            // Check if this client is the local owner
            _isLocalOwner = networkObject.MyPlayerId == networkObject.ownerNetId;

            #region Setup            
            // Initial setup - only do this once
            if ((_isLocalOwner || networkObject.IsServer) && !_setup)
            {
                // Interpolation on the predicted client and server does weird things
                // Only interpolate on non-owner clients
                networkObject.positionInterpolation.Enabled = false;
                networkObject.rotationInterpolation.Enabled = false;
                _setup = true;
            }

            // Get the input listener if it doesn't exist and this isn't a remote client
            if (_inputListener == null) _inputListener = FindObjectsOfType<InputListener>().FirstOrDefault(x => x.networkObject.Owner.NetworkId == networkObject.ownerNetId);
            #endregion

            #region Netcode Logic
            // Server Authority - snap the position on all clients to the server's position
            if (!networkObject.IsServer)
            {
                _rigidBody.position = networkObject.position;
                _rigidBody.rotation = networkObject.rotation;   // DKE: added 
            }

            // Client owner Reconciliation & Prediction
            if (_isLocalOwner)
            {
                if (_inputListener != null)
                {
                    // Reconciliation - only do this if the server update is current or new
                    if (networkObject.frame != 0 && _lastNetworkFrame <= networkObject.frame)
                    {
                        _lastNetworkFrame = networkObject.frame;
                        Reconcile();
                    }

                    // Prediction
                    if (_inputListener.FramesToPlay.Count > 0)
                    {
                        InputFrame input = _inputListener.FramesToPlay[0];
                        _lastLocalFrame = input.frameNumber;
                        PlayerUpdate(input);
                        _inputListener.FramesToPlay.RemoveAt(0);
                    }
                }
            }
            // Server Processing
            else if (networkObject.IsServer)
            {
                // Reset the current input - we don't want to re-use it if there no inputs in the queue
                _currentInput = null;

                if (_inputListener != null)
                {
                    // Process all available inputs each frame
                    while (_inputListener.FramesToPlay.Count > 0)
                    {
                        _currentInput = _inputListener.FramesToPlay[0];
                        _lastLocalFrame = _currentInput.frameNumber;
                        _inputListener.FramesToPlay.RemoveAt(0);

                        // Try-catch is a good idea to handle weird serialization/deserialization errors
                        try
                        {
                            PlayerUpdate(_currentInput);
                        }
                        catch (Exception e)
                        {
                            Debug.LogError(e + " (Serverside input processing - Player.cs line 104)");
                        }
                    }
                }
            }
            #endregion
        }

        private void Move(InputFrame input)
        {
            _rigidBody.velocity = new Vector2(input.horizontal, input.vertical) * Speed * Time.fixedDeltaTime;
            _rigidBody.position += _rigidBody.velocity;
        }


        private void MoveTank(InputFrame input)
        {
            // Create a vector in the direction the tank is facing with a magnitude based on the input, speed and the time between frames.
            Vector3 movement = transform.forward * input.vertical * 5 * Time.deltaTime;

            // Apply this movement to the rigidbody's position.
            _rigidBody.MovePosition(_rigidBody.position + movement);
        }


        private void TurnTank(InputFrame input)
        {
            // Determine the number of degrees to be turned based on the input, speed and time between frames.
            float turn = input.horizontal * 40 * Time.deltaTime;

            // Make this into a rotation in the y axis.
            Quaternion turnRotation = Quaternion.Euler(0f, turn, 0f);

            // Apply this rotation to the rigidbody's rotation.
            _rigidBody.MoveRotation(_rigidBody.rotation * turnRotation);
        }

        private void PhysicsCollisions()
        {
            /*

            // Collision detection - get a list of colliders the player's collider overlaps with
            int numColliders = Physics2D.OverlapCollider(_collider2D, _noFilter, _collisions);

            // Collision Resolution - for each of these colliders check if that collider and the player overlap
            for (int i = 0; i < numColliders; ++i)
            {
                ColliderDistance2D overlap = _collider2D.Distance(_collisions[i]);

                // If the colliders overlap move the player
                if (overlap.isOverlapped) _rigidBody.position += overlap.normal * overlap.distance;
            }

            */
        }

        private void PlayerUpdate(InputFrame input)
        {
            // Set the velocity to zero, move the player based on the next input, then detect & resolve collisions
            _rigidBody.velocity = Vector2.zero;
            if (input != null && input.HasInput)
            {
                //Move(input);      // DKE: removed
                MoveTank(input);    // DKE: Replaced Move by MoveTank + TurnTank
                TurnTank(input);
            }
            PhysicsCollisions();
        }

        private void Reconcile()
        {
            // Remove any inputs up to and including the last input processed by the server
            _inputListener.FramesToReconcile.RemoveAll(f => f.frameNumber <= networkObject.frame);
            
            // Replay them all back to the last input processed by client prediction
            if (_inputListener.FramesToReconcile.Count > 0)
            {
                foreach (InputFrame input in _inputListener.FramesToReconcile)
                {
                    // Don't replay frames that haven't been predicted yet if there are any
                    if (input.frameNumber > _lastLocalFrame) break;
                    PlayerUpdate(input);
                }
            }
        }
    }
}
