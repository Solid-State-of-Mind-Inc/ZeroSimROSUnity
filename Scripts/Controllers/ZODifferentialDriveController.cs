﻿using System;
using System.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using ZO.ROS.MessageTypes;
using ZO.ROS.MessageTypes.Geometry;
using ZO.ROS.MessageTypes.Nav;
using ZO.ROS.MessageTypes.Std;
using ZO.ROS;
using ZO.ROS.Unity;
using ZO.Util;

namespace ZO.Controllers {

    /// <summary>
    /// Overview:     
    /// --------
    /// Controller for differential drive wheel systems. Control is in the 
    /// form of a velocity command, that is split then sent on the two wheels 
    /// of a differential drive wheel base. Odometry is computed from the 
    /// feedback from the hardware, and published.
    ///  
    /// Velocity commands:
    /// ------------------
    /// The controller works with a velocity twist from which it extracts 
    /// the x component of the linear velocity and the z component of the angular 
    /// velocity. Velocities on other components are ignored. 
    /// </summary>
    /// <reference>
    /// See: https://github.com/ros-controls/ros_controllers/blob/indigo-devel/diff_drive_controller/include/diff_drive_controller/diff_drive_controller.h
    /// </reference>
    public class ZODifferentialDriveController : ZOGameObjectBase {

        public Rigidbody _connectedBody;
        public ZO.Physics.ZOHingeJoint _rightWheelMotor;
        public ZO.Physics.ZOHingeJoint _leftWheelMotor;
        public float _wheelRadius = 0;
        public float _wheelSeperation = 0;

        public ZO.Physics.ZOSpeedLimiter _speedLimiterLinear;
        public ZO.Physics.ZOSpeedLimiter _speedLimiterAngular;

        private float _linearVelocity = 0;
        private float _angularVelocity = 0;

        // NOTE: this can only be called in FixedUpdate
        public float LinearVelocity {
            set {
                _linearVelocity = value;
                UpdateMotors();
            }
            get => _linearVelocity;
        }

        // NOTE: this can only be called in FixedUpdate
        public float AngularVelocity {
            set {
                _angularVelocity = value;
                UpdateMotors();
            }
            get => _angularVelocity;
        }

        // Start is called before the first frame update
        protected override void ZOStart() {
            // auto-connect to ROS Bridge connection and disconnect events
            ZOROSUnityManager.Instance.ROSBridgeConnectEvent.AddListener(OnROSBridgeConnected);
            ZOROSUnityManager.Instance.ROSBridgeDisconnectEvent.AddListener(OnROSBridgeConnected);
        }

        protected override void ZOFixedUpdate() {
            // update the motors
            LinearVelocity = (float)_twistMessage.linear.x * Mathf.Rad2Deg;
            AngularVelocity = (float)_twistMessage.angular.z * Mathf.Rad2Deg;
        }
        protected override void ZOFixedUpdateHzSynchronized() {

            // publish odometry
            if (ROSBridgeConnection != null) {
                if (ROSBridgeConnection.IsConnected) {

                    // NOTE: Just echoing back the true odometry.  
                    // TODO: calculat the odometry see: CalculateOdometryOpenLoop
                    _odometryMessage.Update(); // update times stamps

                    // BUGBUG: not super clear on where the pose should be?
                    _odometryMessage.pose.pose.GlobalUnityTransform = _connectedBody.transform;

                    // get velocity in /odom frame
                    Vector3 linear = _connectedBody.velocity;
                    _odometryMessage.twist.twist.angular.z = -_connectedBody.angularVelocity.y; // NOTE: negating velocity?

                    float yaw = _connectedBody.transform.localRotation.eulerAngles.y * Mathf.Deg2Rad;
                    _odometryMessage.twist.twist.linear.x = Mathf.Cos(yaw) * linear.z + Mathf.Sin(yaw) * linear.x;
                    _odometryMessage.twist.twist.linear.y = Mathf.Cos(yaw) * linear.x - Mathf.Sin(yaw) * linear.z;
                    // set covariance
                    // see: https://robotics.stackexchange.com/questions/15265/ros-gazebo-odometry-issue
                    // # Odometry covariances for the encoder output of the robot. These values should
                    // # be tuned to your robot's sample odometry data, but these values are a good place
                    // # to start
                    // pose_covariance_diagonal : [0.001, 0.001, 1000000.0, 1000000.0, 1000000.0, 1000.0]
                    // twist_covariance_diagonal: [0.001, 0.001, 1000000.0, 1000000.0, 1000000.0, 1000.0]
                    _odometryMessage.pose.covariance[0] = 1e-3;
                    _odometryMessage.pose.covariance[7] = 1e-3;
                    _odometryMessage.pose.covariance[14] = 1e6;
                    _odometryMessage.pose.covariance[21] = 1e6;
                    _odometryMessage.pose.covariance[28] = 1e6;
                    _odometryMessage.pose.covariance[35] = 1e3;

                    _odometryMessage.twist.covariance[0] = 1e-3;
                    _odometryMessage.twist.covariance[7] = 1e-3;
                    _odometryMessage.twist.covariance[14] = 1e6;
                    _odometryMessage.twist.covariance[21] = 1e6;
                    _odometryMessage.twist.covariance[28] = 1e6;
                    _odometryMessage.twist.covariance[35] = 1e3;

                    // BUGBUG: not super clear on this being a child of map?
                    _odometryMessage.header.frame_id = "map";

                    ROSBridgeConnection.Publish<OdometryMessage>(_odometryMessage, "/odom");
                }
            }

            // todo broadcast various joint transforms
            // see: https://github.com/ros-simulation/gazebo_ros_pkgs/blob/kinetic-devel/gazebo_plugins/src/gazebo_ros_diff_drive.cpp
            /*
void GazeboRosDiffDrive::publishWheelTF()
{
    ros::Time current_time = ros::Time::now();
    for ( int i = 0; i < 2; i++ ) {

        std::string wheel_frame = gazebo_ros_->resolveTF(joints_[i]->GetChild()->GetName ());
        std::string wheel_parent_frame = gazebo_ros_->resolveTF(joints_[i]->GetParent()->GetName ());

#if GAZEBO_MAJOR_VERSION >= 8
        ignition::math::Pose3d poseWheel = joints_[i]->GetChild()->RelativePose();
#else
        ignition::math::Pose3d poseWheel = joints_[i]->GetChild()->GetRelativePose().Ign();
#endif

        tf::Quaternion qt ( poseWheel.Rot().X(), poseWheel.Rot().Y(), poseWheel.Rot().Z(), poseWheel.Rot().W() );
        tf::Vector3 vt ( poseWheel.Pos().X(), poseWheel.Pos().Y(), poseWheel.Pos().Z() );

        tf::Transform tfWheel ( qt, vt );
        transform_broadcaster_->sendTransform (
            tf::StampedTransform ( tfWheel, current_time, wheel_parent_frame, wheel_frame ) );
    }
}            */

        }
        private void UpdateMotors() {
            float leftVelocity = (LinearVelocity - AngularVelocity * _wheelSeperation / 2.0f) / _wheelRadius;
            float rightVelocity = (LinearVelocity + AngularVelocity * _wheelSeperation / 2.0f) / _wheelRadius;

            _rightWheelMotor.AngularVelocityDegrees = rightVelocity;
            _leftWheelMotor.AngularVelocityDegrees = leftVelocity;
        }

        private float _odomHeading = 0;
        private float _odomX = 0;
        private float _odomY = 0;

        /// <summary>
        /// See: https://github.com/ros-controls/ros_controllers/blob/noetic-devel/diff_drive_controller/src/odometry.cpp 
        /// </summary>
        private void CalculateOdometryOpenLoop() {
            /* see: https://github.com/ros-simulation/gazebo_ros_pkgs/blob/kinetic-devel/gazebo_plugins/src/gazebo_ros_diff_drive.cpp
void GazeboRosDiffDrive::UpdateOdometryEncoder()
{
    double vl = joints_[LEFT]->GetVelocity ( 0 );
    double vr = joints_[RIGHT]->GetVelocity ( 0 );
#if GAZEBO_MAJOR_VERSION >= 8
    common::Time current_time = parent->GetWorld()->SimTime();
#else
    common::Time current_time = parent->GetWorld()->GetSimTime();
#endif
    double seconds_since_last_update = ( current_time - last_odom_update_ ).Double();
    last_odom_update_ = current_time;

    double b = wheel_separation_;

    // Book: Sigwart 2011 Autonompus Mobile Robots page:337
    double sl = vl * ( wheel_diameter_ / 2.0 ) * seconds_since_last_update;
    double sr = vr * ( wheel_diameter_ / 2.0 ) * seconds_since_last_update;
    double ssum = sl + sr;

    double sdiff;
    if(legacy_mode_)
    {
      sdiff = sl - sr;
    }
    else
    {

      sdiff = sr - sl;
    }

    double dx = ( ssum ) /2.0 * cos ( pose_encoder_.theta + ( sdiff ) / ( 2.0*b ) );
    double dy = ( ssum ) /2.0 * sin ( pose_encoder_.theta + ( sdiff ) / ( 2.0*b ) );
    double dtheta = ( sdiff ) /b;

    pose_encoder_.x += dx;
    pose_encoder_.y += dy;
    pose_encoder_.theta += dtheta;

    double w = dtheta/seconds_since_last_update;
    double v = sqrt ( dx*dx+dy*dy ) /seconds_since_last_update;

    tf::Quaternion qt;
    tf::Vector3 vt;
    qt.setRPY ( 0,0,pose_encoder_.theta );
    vt = tf::Vector3 ( pose_encoder_.x, pose_encoder_.y, 0 );

    odom_.pose.pose.position.x = vt.x();
    odom_.pose.pose.position.y = vt.y();
    odom_.pose.pose.position.z = vt.z();

    odom_.pose.pose.orientation.x = qt.x();
    odom_.pose.pose.orientation.y = qt.y();
    odom_.pose.pose.orientation.z = qt.z();
    odom_.pose.pose.orientation.w = qt.w();

    odom_.twist.twist.angular.z = w;
    odom_.twist.twist.linear.x = dx/seconds_since_last_update;
    odom_.twist.twist.linear.y = dy/seconds_since_last_update;
}            
            */
            float dt = Time.deltaTime;

            if (Mathf.Abs(AngularVelocity) < 1e-6f) {
                // Runge-Kutta 2nd order integration
                float direction = _odomHeading + AngularVelocity * 0.5f;
                _odomX += LinearVelocity * Mathf.Cos(direction);
                _odomY += LinearVelocity * Mathf.Sin(direction);
                _odomHeading += AngularVelocity;
            } else {
                float headingOld = _odomHeading;
                float r = LinearVelocity / AngularVelocity;
                _odomHeading += AngularVelocity;
                _odomX += r * (Mathf.Sin(_odomHeading) - Mathf.Sin(headingOld));
                _odomY += r * (Mathf.Cos(_odomHeading) - Mathf.Cos(headingOld));
            }
        }

        /// <summary>
        /// ROS specific functionality
        /// To test: `rosrun turtlebot3 turtlebot3_teleop_key`
        /// </summary>
        public string _ROSTopicSubscription = "/cmd_vel";
        public string _Id = "unity_diff_control";
        private ZOROSBridgeConnection ROSBridgeConnection { get; set; }
        private TwistMessage _twistMessage = new TwistMessage();
        private OdometryMessage _odometryMessage = new OdometryMessage();


        public void OnROSBridgeConnected(ROS.Unity.ZOROSUnityManager rosUnityManager, ZOROSBridgeConnection rosBridgeConnection) {
            Debug.Log("INFO: ZODifferentialDriveController::OnROSBridgeConnected");
            ROSBridgeConnection = rosBridgeConnection;

            // subscribe to Twist Message
            ROSBridgeConnection.Subscribe<TwistMessage>(_Id, _ROSTopicSubscription, _twistMessage.MessageType, OnROSTwistMessageReceived);

            // adverise Odometry Message
            ROSBridgeConnection.Advertise("/odom", _odometryMessage.MessageType);
        }

        public void OnROSBridgeDisconnected(ROS.Unity.ZOROSUnityManager rosUnityManager, ZOROSBridgeConnection rosBridgeConnection) {
            Debug.Log("INFO: ZOImagePublisher::OnROSBridgeDisconnected");
        }

        public Task OnROSTwistMessageReceived(ZOROSBridgeConnection rosBridgeConnection, ZOROSMessageInterface msg) {
            _twistMessage = (TwistMessage)msg;
            // Debug.Log("INFO: Twist Message Received: linear " + _twistMessage.linear.ToString() + " angular: " + _twistMessage.angular.ToString());

            return Task.CompletedTask;
        }


    }

}