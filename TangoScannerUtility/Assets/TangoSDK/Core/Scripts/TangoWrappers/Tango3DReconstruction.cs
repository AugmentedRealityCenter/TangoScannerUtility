﻿//-----------------------------------------------------------------------
// <copyright file="Tango3DReconstruction.cs" company="Google">
//
// Copyright 2016 Google Inc. All Rights Reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// </copyright>
//-----------------------------------------------------------------------

namespace Tango
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Runtime.InteropServices;
    using Tango;
    using UnityEngine;

    /// <summary>
    /// Delegate for Tango 3D Reconstruction GridIndicesDirty events.
    /// </summary>
    /// <param name="gridIndexList">List of GridIndex objects that are dirty and should be updated.</param>
    internal delegate void OnTango3DReconstructionGridIndiciesDirtyEventHandler(List<Tango3DReconstruction.GridIndex> gridIndexList);

    /// <summary>
    /// Manages a single instance of the Tango 3D Reconstruction library, updating a single 3D model based on depth
    /// and color information.
    /// </summary>
    public class Tango3DReconstruction : IDisposable, ITangoLifecycle, ITangoDepthMultithreaded, ITangoVideoOverlayMultithreaded
    {
        /// <summary>
        /// If set 3D reconstruction will happen in the area description's reference frame.
        /// </summary>
        internal bool m_useAreaDescriptionPose;

        /// <summary>
        /// If set, 3D reconstruction will pass color information into the reconstruction.
        /// </summary>
        internal bool m_sendColorToUpdate;

        /// <summary>
        /// The handle for the Tango 3D Reconstruction library.
        /// </summary>
        private IntPtr m_context;

        /// <summary>
        /// Grid indices that have been updated since the last call to SendEventIfAvailable.
        /// </summary>
        private List<GridIndex> m_updatedIndices = new List<GridIndex>();

        /// <summary>
        /// Synchronization object.
        /// </summary>
        private object m_lockObject = new object();

        /// <summary>
        /// Called when the 3D reconstruction is dirty.
        /// </summary>
        private OnTango3DReconstructionGridIndiciesDirtyEventHandler m_onGridIndicesDirty;

        /// <summary>
        /// Constant matrix for the transformation from the device frame to the depth camera frame.
        /// </summary>
        private Matrix4x4 m_device_T_depthCamera;

        /// <summary>
        /// Constant matrix for the transformation from the device frame to the color camera frame.
        /// </summary>
        private Matrix4x4 m_device_T_colorCamera;

        /// <summary>
        /// Constant matrix converting start of service frame to Unity world frame.
        /// </summary>
        private Matrix4x4 m_unityWorld_T_startService;

        /// <summary>
        /// Constant calibration data for the color camera.
        /// </summary>
        private APICameraCalibration m_colorCameraIntrinsics;

        /// <summary>
        /// Cache of the most recent depth received, to send with color information.
        /// </summary>
        private APIPointCloud m_mostRecentDepth;

        /// <summary>
        /// Cache of the most recent depth received's points.
        /// 
        /// This is a separate array so the code can use Marshal.Copy.
        /// </summary>
        private float[] m_mostRecentDepthPoints = new float[TangoUnityDepth.MAX_POINTS_ARRAY_SIZE];

        /// <summary>
        /// Cache of the most recent depth received's pose, to send with color information.
        /// </summary>
        private APIPose m_mostRecentDepthPose;

        /// <summary>
        /// If set, <c>m_mostRecentDepth</c> and <c>m_mostRecentDepthPose</c> are set and should be sent to
        /// reconstruction once combined with other data.
        /// </summary>
        private bool m_mostRecentDepthIsValid;

        /// <summary>
        /// If true, 3D reconstruction will be updated with depth.  Otherwise, it will not.
        /// </summary>
        private bool m_enabled = true;

        /// <summary>
        /// Initializes a new instance of the <see cref="Tango3DReconstruction"/> class.
        /// </summary>
        /// <param name="resolution">Size in meters of each grid cell.</param>
        /// <param name="generateColor">If true the reconstruction will contain color information.</param>
        /// <param name="spaceClearing">If true the reconstruction will clear empty space it detects.</param> 
        internal Tango3DReconstruction(float resolution, bool generateColor, bool spaceClearing)
        {
            IntPtr config = API.Tango3DR_Config_create();
            API.Tango3DR_Config_setDouble(config, "resolution", resolution);
            API.Tango3DR_Config_setBool(config, "generate_color", generateColor);
            API.Tango3DR_Config_setBool(config, "use_space_clearing", spaceClearing);

            m_context = API.Tango3DR_create(config);
            API.Tango3DR_Config_destroy(config);
        }

        /// <summary>
        /// Corresponds to a Tango3DR_Status.
        /// </summary>
        public enum Status
        {
            ERROR = -3,
            INSUFFICIENT_SPACE = -2,
            INVALID = -1,
            SUCCESS = 0
        }

        /// <summary>
        /// Releases all resource used by the <see cref="Tango3DReconstruction"/> object.
        /// </summary>
        /// <remarks>Call <see cref="Dispose"/> when you are finished using the <see cref="Tango3DReconstruction"/>.
        /// The <see cref="Dispose"/> method leaves the <see cref="Tango3DReconstruction"/> in an unusable state. After 
        /// calling <see cref="Dispose"/>, you must release all references to the <see cref="Tango3DReconstruction"/> 
        /// so the garbage collector can reclaim the memory that the <see cref="Tango3DReconstruction"/> was occupying.
        /// </remarks>
        public void Dispose()
        {
            if (m_context != IntPtr.Zero)
            {
                API.Tango3DR_destroy(m_context);
            }

            m_context = IntPtr.Zero;
        }

        /// <summary>
        /// This is called when the permission granting process is finished.
        /// </summary>
        /// <param name="permissionsGranted"><c>true</c> if permissions were granted, otherwise <c>false</c>.</param>
        /// <c>false</c>
        public void OnTangoPermissions(bool permissionsGranted)
        {
            // Nothing to do.
        }

        /// <summary>
        /// This is called when succesfully connected to the Tango service.
        /// </summary>
        public void OnTangoServiceConnected()
        {
            _UpdateExtrinsics();
        }

        /// <summary>
        /// This is called when disconnected from the Tango service.
        /// </summary>
        public void OnTangoServiceDisconnected()
        {
            // Nothing to do.
        }

        /// <summary>
        /// This is called each time new depth data is available.
        /// 
        /// On the Tango tablet, the depth callback occurs at 5 Hz.
        /// </summary>
        /// <param name="tangoDepth">Tango depth.</param>
        public void OnTangoDepthMultithreadedAvailable(TangoXYZij tangoDepth)
        {
            if (!m_enabled)
            {
                return;
            }

            // Build World T depth camera
            TangoPoseData world_T_devicePose = new TangoPoseData();
            if (m_useAreaDescriptionPose)
            {
                TangoCoordinateFramePair pair;
                pair.baseFrame = TangoEnums.TangoCoordinateFrameType.TANGO_COORDINATE_FRAME_AREA_DESCRIPTION;
                pair.targetFrame = TangoEnums.TangoCoordinateFrameType.TANGO_COORDINATE_FRAME_DEVICE;
                PoseProvider.GetPoseAtTime(world_T_devicePose, tangoDepth.timestamp, pair);
            }
            else
            {
                TangoCoordinateFramePair pair;
                pair.baseFrame = TangoEnums.TangoCoordinateFrameType.TANGO_COORDINATE_FRAME_START_OF_SERVICE;
                pair.targetFrame = TangoEnums.TangoCoordinateFrameType.TANGO_COORDINATE_FRAME_DEVICE;
                PoseProvider.GetPoseAtTime(world_T_devicePose, tangoDepth.timestamp, pair);
            }

            if (world_T_devicePose.status_code != TangoEnums.TangoPoseStatusType.TANGO_POSE_VALID)
            {
                Debug.Log(string.Format("Time {0} has bad status code {1}", 
                                        tangoDepth.timestamp, world_T_devicePose.status_code)
                          + Environment.StackTrace);
                return;
            }

            // NOTE: The 3D Reconstruction library does not handle left handed matrices correctly.  For now, transform
            // into the Unity world space after extraction.
            Matrix4x4 world_T_depthCamera = world_T_devicePose.ToMatrix4x4() * m_device_T_depthCamera;

            _UpdateDepth(tangoDepth, world_T_depthCamera);
        }

        /// <summary>
        /// This will be called when a new frame is available from the camera.
        /// 
        /// The first scan-line of the color image is reserved for metadata instead of image pixels.
        /// </summary>
        /// <param name="cameraId">Camera identifier.</param>
        /// <param name="imageBuffer">Image buffer.</param>
        public void OnTangoImageMultithreadedAvailable(TangoEnums.TangoCameraId cameraId, TangoImageBuffer imageBuffer)
        {
            if (!m_enabled)
            {
                return;
            }

            if (cameraId != TangoEnums.TangoCameraId.TANGO_CAMERA_COLOR)
            {
                return;
            }

            // Build World T depth camera
            TangoPoseData world_T_devicePose = new TangoPoseData();
            if (m_useAreaDescriptionPose)
            {
                TangoCoordinateFramePair pair;
                pair.baseFrame = TangoEnums.TangoCoordinateFrameType.TANGO_COORDINATE_FRAME_AREA_DESCRIPTION;
                pair.targetFrame = TangoEnums.TangoCoordinateFrameType.TANGO_COORDINATE_FRAME_DEVICE;
                PoseProvider.GetPoseAtTime(world_T_devicePose, imageBuffer.timestamp, pair);
            }
            else
            {
                TangoCoordinateFramePair pair;
                pair.baseFrame = TangoEnums.TangoCoordinateFrameType.TANGO_COORDINATE_FRAME_START_OF_SERVICE;
                pair.targetFrame = TangoEnums.TangoCoordinateFrameType.TANGO_COORDINATE_FRAME_DEVICE;
                PoseProvider.GetPoseAtTime(world_T_devicePose, imageBuffer.timestamp, pair);
            }

            if (world_T_devicePose.status_code != TangoEnums.TangoPoseStatusType.TANGO_POSE_VALID)
            {
                Debug.Log(string.Format("Time {0} has bad status code {1}", 
                                        imageBuffer.timestamp, world_T_devicePose.status_code)
                          + Environment.StackTrace);
                return;
            }

            // NOTE: The 3D Reconstruction library does not handle left handed matrices correctly.  For now, transform
            // into the Unity world space after extraction.
            Matrix4x4 world_T_colorCamera = world_T_devicePose.ToMatrix4x4() * m_device_T_colorCamera;

            _UpdateColor(imageBuffer, world_T_colorCamera);
        }

        /// <summary>
        /// Set if the 3DReconstruction is enabled or not.  If disabled, the 3D reconstruction will not get updated.
        /// </summary>
        /// <param name="enabled">If set to <c>true</c> enabled.</param>
        internal void SetEnabled(bool enabled)
        {
            m_enabled = enabled;
        }

        /// <summary>
        /// Register a Unity main thread handler for the GridIndicesDirty event.
        /// </summary>
        /// <param name="handler">Event handler to register.</param>
        internal void RegisterGridIndicesDirty(OnTango3DReconstructionGridIndiciesDirtyEventHandler handler)
        {
            if (handler != null)
            {
                m_onGridIndicesDirty += handler;
            }
        }
        
        /// <summary>
        /// Unregister a Unity main thread handler for the Tango depth event.
        /// </summary>
        /// <param name="handler">Event handler to unregister.</param>
        internal void UnregisterGridIndicesDirty(OnTango3DReconstructionGridIndiciesDirtyEventHandler handler)
        {
            if (handler != null)
            {
                m_onGridIndicesDirty -= handler;
            }
        }

        /// <summary>
        /// Raise ITango3DReconstruction events if there is new data.
        /// </summary>
        internal void SendEventIfAvailable()
        {
            lock (m_lockObject)
            {
                if (m_updatedIndices.Count != 0)
                {
                    if (m_onGridIndicesDirty != null)
                    {
                        m_onGridIndicesDirty(m_updatedIndices);
                    }

                    m_updatedIndices.Clear();
                }
            }
        }

        /// <summary>
        /// Extract a mesh for a single grid index, into a suitable format for Unity Mesh.
        /// </summary>
        /// <returns>
        /// Returns Status.SUCCESS if the mesh is fully extracted and stored in the arrays.  In this case, numVertices 
        /// and numTriangles will say how many vertices and triangles are used, the rest of the array is untouched.
        /// 
        /// Returns Status.INSUFFICIENT_SPACE if the mesh is partially extracted and stored in the arrays.  numVertices 
        /// and numTriangles have the same meaning as if Status.SUCCESS is returned, but in this case the array should 
        /// grow.
        /// 
        /// Returns Status.ERROR or Status.INVALID if some other error occurs.
        /// </returns>
        /// <param name="gridIndex">Grid index to extract.</param>
        /// <param name="vertices">On successful extraction this will get filled out with the vertex positions.</param>
        /// <param name="normals">On successful extraction this will get filled out whith vertex normals.</param>
        /// <param name="colors">On successful extraction this will get filled out with vertex colors.</param>
        /// <param name="triangles">On succesful extraction this will get filled out with vertex indexes.</param>
        /// <param name="numVertices">Number of vertexes filled out.</param>
        /// <param name="numTriangles">Number of triangles filled out.</param>
        internal Status ExtractMeshSegment(
            GridIndex gridIndex, Vector3[] vertices, Vector3[] normals, Color32[] colors, int[] triangles,
            out int numVertices, out int numTriangles)
        {
            numVertices = 0;
            numTriangles = 0;
            int result = API.Tango3DR_extractPreallocatedMeshSegment(
                m_context, ref gridIndex, vertices.Length, triangles.Length / 3, vertices, triangles, normals, colors,
                out numVertices, out numTriangles);

            // NOTE: The 3D Reconstruction library does not handle left handed matrices correctly.  For now, transform
            // into the Unity world space after extraction and account for winding order changes.
            for (int it = 0; it < numVertices; ++it)
            {
                vertices[it] = m_unityWorld_T_startService.MultiplyPoint(vertices[it]);
            }

            for (int it = 0; it < numTriangles; ++it)
            {
                int temp = triangles[(it * 3) + 0];
                triangles[(it * 3) + 0] = triangles[(it * 3) + 1];
                triangles[(it * 3) + 1] = temp;
            }

            return (Status)result;
        }

        /// <summary>
        /// Extract a mesh for the entire 3D reconstruction, into a suitable format for Unity Mesh.
        /// </summary>
        /// <returns>
        /// Returns Status.SUCCESS if the mesh is fully extracted and stored in the arrays.  In this case, numVertices 
        /// and numTriangles will say how many vertices and triangles are used, the rest of the array is untouched.
        /// 
        /// Returns Status.INSUFFICIENT_SPACE if the mesh is partially extracted and stored in the arrays.  numVertices 
        /// and numTriangles have the same meaning as if Status.SUCCESS is returned, but in this case the array should 
        /// grow.
        /// 
        /// Returns Status.ERROR or Status.INVALID if some other error occurs.
        /// </returns>
        /// <param name="vertices">On successful extraction this will get filled out with the vertex positions.</param>
        /// <param name="normals">On successful extraction this will get filled out whith vertex normals.</param>
        /// <param name="colors">On successful extraction this will get filled out with vertex colors.</param>
        /// <param name="triangles">On succesful extraction this will get filled out with vertex indexes.</param>
        /// <param name="numVertices">Number of vertexes filled out.</param>
        /// <param name="numTriangles">Number of triangles filled out.</param>
        internal Status ExtractWholeMesh(
            Vector3[] vertices, Vector3[] normals, Color32[] colors, int[] triangles, out int numVertices,
            out int numTriangles)
        {
            numVertices = 0;
            numTriangles = 0;

            int result = API.Tango3DR_extractPreallocatedFullMesh(
                m_context, vertices.Length, triangles.Length / 3, vertices, triangles, normals, colors,
                out numVertices, out numTriangles);
            return (Status)result;
        }

        /// <summary>
        /// Clear the current mesh in the 3D reconstruction.
        /// </summary>
        internal void Clear()
        {
            int result = API.Tango3DR_clear(m_context);
            if ((Status)result != Status.SUCCESS)
            {
                Debug.Log("Tango3DR_clear returned non-success." + Environment.StackTrace);
            }
        }

        /// <summary>
        /// Update the 3D Reconstruction with a new point cloud and pose.
        /// 
        /// It is expected this will get called in from the Tango binder thread.
        /// </summary>
        /// <param name="depth">Point cloud from Tango.</param>
        /// <param name="depthPose">Pose matrix the point cloud corresponds too.</param>
        private void _UpdateDepth(TangoXYZij depth, Matrix4x4 depthPose)
        {
            if (m_context == IntPtr.Zero)
            {
                Debug.Log("Update called before creating a reconstruction context." + Environment.StackTrace);
                return;
            }

            APIPointCloud apiCloud;
            apiCloud.numPoints = depth.xyz_count;
            apiCloud.points = depth.xyz;
            apiCloud.timestamp = depth.timestamp;
            
            APIPose apiDepthPose = APIPose.FromMatrix4x4(ref depthPose);

            if (!m_sendColorToUpdate)
            {
                // No need to wait for a color image, update reconstruction immediately.
                IntPtr rawUpdatedIndices;
                Status result = (Status)API.Tango3DR_update(
                    m_context, ref apiCloud, ref apiDepthPose, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    out rawUpdatedIndices);
                if (result != Status.SUCCESS)
                {
                    Debug.Log("Tango3DR_update returned non-success." + Environment.StackTrace);
                    return;
                }

                _AddUpdatedIndices(rawUpdatedIndices);
                API.Tango3DR_GridIndexArray_destroy(rawUpdatedIndices);
            }
            else
            {
                lock (m_lockObject)
                {
                    // We need both a color image and a depth cloud to update reconstruction.  Cache the depth cloud
                    // because there are much less depth points than pixels.
                    m_mostRecentDepth = apiCloud;
                    m_mostRecentDepth.points = IntPtr.Zero;
                    m_mostRecentDepthPose = apiDepthPose;

                    Marshal.Copy(apiCloud.points, m_mostRecentDepthPoints, 0, apiCloud.numPoints * 3);
                    m_mostRecentDepthIsValid = true;
                }
            }
        }

        /// <summary>
        /// Update the 3D Reconstruction with a new image and pose.
        /// 
        /// It is expected this will get called in from the Tango binder thread.
        /// </summary>
        /// <param name="image">Color image from Tango.</param>
        /// <param name="imagePose">Pose matrix the color image corresponds too.</param>
        private void _UpdateColor(TangoImageBuffer image, Matrix4x4 imagePose)
        {
            if (!m_sendColorToUpdate)
            {
                // There is no depth cloud to process.
                return;
            }

            if (m_context == IntPtr.Zero)
            {
                Debug.Log("Update called before creating a reconstruction context." + Environment.StackTrace);
                return;
            }

            lock (m_lockObject)
            {
                if (!m_mostRecentDepthIsValid)
                {
                    return;
                }

                APIImageBuffer apiImage;
                apiImage.width = image.width;
                apiImage.height = image.height;
                apiImage.stride = image.stride;
                apiImage.timestamp = image.timestamp;
                apiImage.format = (int)image.format;
                apiImage.data = image.data;

                APIPose apiImagePose = APIPose.FromMatrix4x4(ref imagePose);

                // Update the depth points to have the right value
                GCHandle mostRecentDepthPointsHandle = GCHandle.Alloc(m_mostRecentDepthPoints, GCHandleType.Pinned);
                m_mostRecentDepth.points = Marshal.UnsafeAddrOfPinnedArrayElement(m_mostRecentDepthPoints, 0);

                GCHandle thisHandle = GCHandle.Alloc(this, GCHandleType.Pinned);

                IntPtr rawUpdatedIndices;
                Status result = (Status)API.Tango3DR_update(
                    m_context, ref m_mostRecentDepth, ref m_mostRecentDepthPose,
                    ref apiImage, ref apiImagePose, ref m_colorCameraIntrinsics, 
                    out rawUpdatedIndices);

                m_mostRecentDepthIsValid = false;
                thisHandle.Free();
                mostRecentDepthPointsHandle.Free();

                if (result != Status.SUCCESS)
                {
                    Debug.Log("Tango3DR_update returned non-success." + Environment.StackTrace);
                    return;
                }

                _AddUpdatedIndices(rawUpdatedIndices);
                API.Tango3DR_GridIndexArray_destroy(rawUpdatedIndices);
            }
        }

        /// <summary>
        /// Add to the list of updated GridIndex objects that gets sent to the main thread.
        /// </summary>
        /// <param name="rawUpdatedIndices">IntPtr to a list of updated indices.</param>
        private void _AddUpdatedIndices(IntPtr rawUpdatedIndices)
        {
            int numUpdatedIndices = Marshal.ReadInt32(rawUpdatedIndices, 0);
            IntPtr rawIndices = Marshal.ReadIntPtr(rawUpdatedIndices, 4);
            lock (m_lockObject)
            {
                if (m_updatedIndices.Count == 0)
                {
                    // Update in fast mode, no duplicates are possible.
                    for (int it = 0; it < numUpdatedIndices; ++it)
                    {
                        GridIndex index;
                        index.x = Marshal.ReadInt32(rawIndices, (0 + (it * 3)) * 4);
                        index.y = Marshal.ReadInt32(rawIndices, (1 + (it * 3)) * 4);
                        index.z = Marshal.ReadInt32(rawIndices, (2 + (it * 3)) * 4);
                        m_updatedIndices.Add(index);
                    }
                }
                else
                {
                    // Duplicates are possible, need to check while adding.
                    for (int it = 0; it < numUpdatedIndices; ++it)
                    {
                        GridIndex index;
                        index.x = Marshal.ReadInt32(rawIndices, (0 + (it * 3)) * 4);
                        index.y = Marshal.ReadInt32(rawIndices, (1 + (it * 3)) * 4);
                        index.z = Marshal.ReadInt32(rawIndices, (2 + (it * 3)) * 4);
                        if (!m_updatedIndices.Contains(index))
                        {
                            m_updatedIndices.Add(index);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Calculate the camera extrinsics for this device.
        /// </summary>
        private void _UpdateExtrinsics()
        {
            TangoCoordinateFramePair pair;

            TangoPoseData imu_T_devicePose = new TangoPoseData();
            pair.baseFrame = TangoEnums.TangoCoordinateFrameType.TANGO_COORDINATE_FRAME_IMU;
            pair.targetFrame = TangoEnums.TangoCoordinateFrameType.TANGO_COORDINATE_FRAME_DEVICE;
            PoseProvider.GetPoseAtTime(imu_T_devicePose, 0, pair);

            TangoPoseData imu_T_depthCameraPose = new TangoPoseData();
            pair.baseFrame = TangoEnums.TangoCoordinateFrameType.TANGO_COORDINATE_FRAME_IMU;
            pair.targetFrame = TangoEnums.TangoCoordinateFrameType.TANGO_COORDINATE_FRAME_CAMERA_DEPTH;
            PoseProvider.GetPoseAtTime(imu_T_depthCameraPose, 0, pair);

            TangoPoseData imu_T_colorCameraPose = new TangoPoseData();
            pair.baseFrame = TangoEnums.TangoCoordinateFrameType.TANGO_COORDINATE_FRAME_IMU;
            pair.targetFrame = TangoEnums.TangoCoordinateFrameType.TANGO_COORDINATE_FRAME_CAMERA_COLOR;
            PoseProvider.GetPoseAtTime(imu_T_colorCameraPose, 0, pair);

            // Convert into matrix form to combine the poses.
            Matrix4x4 device_T_imu = Matrix4x4.Inverse(imu_T_devicePose.ToMatrix4x4());
            m_device_T_depthCamera = device_T_imu * imu_T_depthCameraPose.ToMatrix4x4();
            m_device_T_colorCamera = device_T_imu * imu_T_colorCameraPose.ToMatrix4x4();

            m_unityWorld_T_startService.SetColumn(0, new Vector4(1, 0, 0, 0));
            m_unityWorld_T_startService.SetColumn(1, new Vector4(0, 0, 1, 0));
            m_unityWorld_T_startService.SetColumn(2, new Vector4(0, 1, 0, 0));
            m_unityWorld_T_startService.SetColumn(3, new Vector4(0, 0, 0, 1));

            // Update the camera intrinsics too.
            TangoCameraIntrinsics colorCameraIntrinsics = new TangoCameraIntrinsics();
            VideoOverlayProvider.GetIntrinsics(TangoEnums.TangoCameraId.TANGO_CAMERA_COLOR, colorCameraIntrinsics);
            m_colorCameraIntrinsics.calibration_type = (int)colorCameraIntrinsics.calibration_type;
            m_colorCameraIntrinsics.width = colorCameraIntrinsics.width;
            m_colorCameraIntrinsics.height = colorCameraIntrinsics.height;
            m_colorCameraIntrinsics.cx = colorCameraIntrinsics.cx;
            m_colorCameraIntrinsics.cy = colorCameraIntrinsics.cy;
            m_colorCameraIntrinsics.fx = colorCameraIntrinsics.fx;
            m_colorCameraIntrinsics.fy = colorCameraIntrinsics.fy;
            m_colorCameraIntrinsics.distortion0 = colorCameraIntrinsics.distortion0;
            m_colorCameraIntrinsics.distortion1 = colorCameraIntrinsics.distortion1;
            m_colorCameraIntrinsics.distortion2 = colorCameraIntrinsics.distortion2;
            m_colorCameraIntrinsics.distortion3 = colorCameraIntrinsics.distortion3;
            m_colorCameraIntrinsics.distortion4 = colorCameraIntrinsics.distortion4;
        }

        /// <summary>
        /// Indexes into the 3D reconstruction mesh's grid cells.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct GridIndex
        {
            [MarshalAs(UnmanagedType.I4)]
            public Int32 x;

            [MarshalAs(UnmanagedType.I4)]
            public Int32 y;

            [MarshalAs(UnmanagedType.I4)]
            public Int32 z;
        }

        /// <summary>
        /// Corresponds to a Tango3DR_CameraCalibration.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct APICameraCalibration
        {
            [MarshalAs(UnmanagedType.U4)]
            public int calibration_type;

            [MarshalAs(UnmanagedType.U4)]
            public UInt32 width;

            [MarshalAs(UnmanagedType.U4)]
            public UInt32 height;

            [MarshalAs(UnmanagedType.R8)]
            public double fx;

            [MarshalAs(UnmanagedType.R8)]
            public double fy;

            [MarshalAs(UnmanagedType.R8)]
            public double cx;

            [MarshalAs(UnmanagedType.R8)]
            public double cy;

            [MarshalAs(UnmanagedType.R8)]
            public double distortion0;

            [MarshalAs(UnmanagedType.R8)]
            public double distortion1;

            [MarshalAs(UnmanagedType.R8)]
            public double distortion2;

            [MarshalAs(UnmanagedType.R8)]
            public double distortion3;

            [MarshalAs(UnmanagedType.R8)]
            public double distortion4;
        }

        /// <summary>
        /// Corresponds to a Tango3DR_ImageBuffer.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct APIImageBuffer
        {
            [MarshalAs(UnmanagedType.U4)]
            public UInt32 width;

            [MarshalAs(UnmanagedType.U4)]
            public UInt32 height;

            [MarshalAs(UnmanagedType.U4)]
            public UInt32 stride;

            [MarshalAs(UnmanagedType.R8)]
            public double timestamp;

            [MarshalAs(UnmanagedType.I4)]
            public int format;

            public IntPtr data;
        }

        /// <summary>
        /// Corresponds to a Tango3DR_PointCloud.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct APIPointCloud
        {
            [MarshalAs(UnmanagedType.R8)]
            public double timestamp;

            [MarshalAs(UnmanagedType.I4)]
            public Int32 numPoints;

            public IntPtr points;
        }

        /// <summary>
        /// Corresponds to a Tango3DR_Pose.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct APIPose
        {
            [MarshalAs(UnmanagedType.R8)]
            public double translation0;

            [MarshalAs(UnmanagedType.R8)]
            public double translation1;

            [MarshalAs(UnmanagedType.R8)]
            public double translation2;

            [MarshalAs(UnmanagedType.R8)]
            public double orientation0;

            [MarshalAs(UnmanagedType.R8)]
            public double orientation1;

            [MarshalAs(UnmanagedType.R8)]
            public double orientation2;

            [MarshalAs(UnmanagedType.R8)]
            public double orientation3;

            /// <summary>
            /// Initializes the <see cref="Tango.Tango3DReconstruction+APIPose"/> struct.
            /// </summary>
            /// <param name="matrix">Right handed matrix to represent.</param>
            /// <returns>APIPose for the matrix.</returns>
            public static APIPose FromMatrix4x4(ref Matrix4x4 matrix)
            {
                Vector3 position = matrix.GetColumn(3);
                Quaternion orientation = Quaternion.LookRotation(matrix.GetColumn(2), matrix.GetColumn(1));

                APIPose pose;
                pose.translation0 = position.x;
                pose.translation1 = position.y;
                pose.translation2 = position.z;
                pose.orientation0 = orientation.x;
                pose.orientation1 = orientation.y;
                pose.orientation2 = orientation.z;
                pose.orientation3 = orientation.w;
                return pose;
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.DocumentationRules",
                                                         "SA1600:ElementsMustBeDocumented",
                                                         Justification = "C API Wrapper.")]
        private static class API
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            private const string TANGO_3DR_DLL = "tango_3d_reconstruction";

            [DllImport(TANGO_3DR_DLL)]
            public static extern IntPtr Tango3DR_create(IntPtr config);

            [DllImport(TANGO_3DR_DLL)]
            public static extern int Tango3DR_destroy(IntPtr context);

            [DllImport(TANGO_3DR_DLL)]
            public static extern IntPtr Tango3DR_Config_create();

            [DllImport(TANGO_3DR_DLL)]
            public static extern int Tango3DR_Config_destroy(IntPtr config);

            [DllImport(TANGO_3DR_DLL)]
            public static extern int Tango3DR_Config_setBool(IntPtr config, string key, bool value);

            [DllImport(TANGO_3DR_DLL)]
            public static extern int Tango3DR_Config_setInt32(IntPtr config, string key, Int32 value);

            [DllImport(TANGO_3DR_DLL)]
            public static extern int Tango3DR_Config_setDouble(IntPtr config, string key, double value);

            [DllImport(TANGO_3DR_DLL)]
            public static extern int Tango3DR_Config_getBool(IntPtr config, string key, out bool value);

            [DllImport(TANGO_3DR_DLL)]
            public static extern int Tango3DR_Config_getInt32(IntPtr config, string key, out Int32 value);

            [DllImport(TANGO_3DR_DLL)]
            public static extern int Tango3DR_Config_getDouble(IntPtr config, string key, out double value);

            [DllImport(TANGO_3DR_DLL)]
            public static extern int Tango3DR_GridIndexArray_destroy(IntPtr gridIndexArray);

            [DllImport(TANGO_3DR_DLL)]
            public static extern int Tango3DR_clear(IntPtr context);

            [DllImport(TANGO_3DR_DLL)]
            public static extern int Tango3DR_update(IntPtr context, ref APIPointCloud cloud, ref APIPose cloud_pose,
                                                     ref APIImageBuffer image, ref APIPose image_pose,
                                                     ref APICameraCalibration calibration, out IntPtr updated_indices);

            [DllImport(TANGO_3DR_DLL)]
            public static extern int Tango3DR_update(IntPtr context, ref APIPointCloud cloud, ref APIPose cloud_pose,
                                                     IntPtr image, IntPtr image_pose, IntPtr calibration, 
                                                     out IntPtr updated_indices);

            [DllImport(TANGO_3DR_DLL)]
            public static extern int Tango3DR_extractPreallocatedMeshSegment(
                IntPtr context, ref GridIndex gridIndex, Int32 maxNumVertices, Int32 maxNumFaces, Vector3[] veritices,
                int[] faces, Vector3[] normals, Color32[] colors, out Int32 numVertices, out Int32 numFaces);

            [DllImport(TANGO_3DR_DLL)]
            public static extern int Tango3DR_extractPreallocatedFullMesh(
                IntPtr context, Int32 maxNumVertices, Int32 maxNumFaces, Vector3[] veritices,
                int[] faces, Vector3[] normals, Color32[] colors, out Int32 numVertices, out Int32 numFaces);

#else
            public static IntPtr Tango3DR_create(IntPtr config)
            {
                return IntPtr.Zero;
            }

            public static int Tango3DR_destroy(IntPtr context)
            {
                return (int)Status.SUCCESS;
            }

            public static IntPtr Tango3DR_Config_create()
            {
                return IntPtr.Zero;
            }

            public static int Tango3DR_Config_destroy(IntPtr config)
            {
                return (int)Status.SUCCESS;
            }

            public static int Tango3DR_Config_setBool(IntPtr config, string key, bool value)
            {
                return (int)Status.SUCCESS;
            }

            public static int Tango3DR_Config_setInt32(IntPtr config, string key, Int32 value)
            {
                return (int)Status.SUCCESS;
            }

            public static int Tango3DR_Config_setDouble(IntPtr config, string key, double value)
            {
                return (int)Status.SUCCESS;
            }

            public static int Tango3DR_Config_getBool(IntPtr config, string key, out bool value)
            {
                value = false;
                return (int)Status.SUCCESS;
            }

            public static int Tango3DR_Config_getInt32(IntPtr config, string key, out Int32 value)
            {
                value = 0;
                return (int)Status.SUCCESS;
            }

            public static int Tango3DR_Config_getDouble(IntPtr config, string key, out double value)
            {
                value = 0;
                return (int)Status.SUCCESS;
            }

            public static int Tango3DR_GridIndexArray_destroy(IntPtr gridIndexArray)
            {
                return (int)Status.SUCCESS;
            }

            public static int Tango3DR_clear(IntPtr context)
            {
                return (int)Status.SUCCESS;
            }

            public static int Tango3DR_update(IntPtr context, ref APIPointCloud cloud, ref APIPose cloud_pose,
                                              ref APIImageBuffer image, ref APIPose image_pose,
                                              ref APICameraCalibration calibration, out IntPtr updated_indices)
            {
                updated_indices = IntPtr.Zero;
                return (int)Status.SUCCESS;
            }

            public static int Tango3DR_update(IntPtr context, ref APIPointCloud cloud, ref APIPose cloud_pose,
                                              IntPtr image, IntPtr image_pose, IntPtr calibration,
                                              out IntPtr updated_indices)
            {
                updated_indices = IntPtr.Zero;
                return (int)Status.SUCCESS;
            }

            public static int Tango3DR_extractPreallocatedMeshSegment(
                IntPtr context, ref GridIndex gridIndex, Int32 maxNumVertices, Int32 maxNumFaces, Vector3[] veritices,
                int[] faces, Vector3[] normals, Color32[] colors, out Int32 numVertices, out Int32 numFaces)
            {
                numVertices = 0;
                numFaces = 0;
                return (int)Status.SUCCESS;
            }

            public static int Tango3DR_extractPreallocatedFullMesh(
                IntPtr context, Int32 maxNumVertices, Int32 maxNumFaces, Vector3[] veritices, int[] faces,
                Vector3[] normals, Color32[] colors, out Int32 numVertices, out Int32 numFaces)
            {
                numVertices = 0;
                numFaces = 0;
                return (int)Status.SUCCESS;
            }
#endif
        }
    }
}
