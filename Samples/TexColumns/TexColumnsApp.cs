﻿using System;
using SharpDX;
using System.Collections.Generic;
using System.Threading;
using SharpDX.Direct3D;
using SharpDX.Direct3D12;
using SharpDX.DXGI;
using Resource = SharpDX.Direct3D12.Resource;
using System.IO;
using System.Globalization;
using ShaderResourceViewDimension = SharpDX.Direct3D12.ShaderResourceViewDimension;

namespace DX12GameProgramming
{
    public class TexColumnsApp : D3DApp
    {
        private readonly List<FrameResource> _frameResources = new List<FrameResource>(NumFrameResources);
        private readonly List<AutoResetEvent> _fenceEvents = new List<AutoResetEvent>(NumFrameResources);
        private int _currFrameResourceIndex;

        private RootSignature _rootSignature;

        private DescriptorHeap _srvDescriptorHeap;
        private DescriptorHeap[] _descriptorHeaps;

        private readonly Dictionary<string, MeshGeometry> _geometries = new Dictionary<string, MeshGeometry>();
        private readonly Dictionary<string, Material> _materials = new Dictionary<string, Material>();
        private readonly Dictionary<string, Texture> _textures = new Dictionary<string, Texture>();
        private readonly Dictionary<string, ShaderBytecode> _shaders = new Dictionary<string, ShaderBytecode>();

        private PipelineState _opaquePso;

        private InputLayoutDescription _inputLayout;

        // List of all the render items.
        private readonly List<RenderItem> _allRitems = new List<RenderItem>();

        // Render items divided by PSO.
        private readonly List<RenderItem> _opaqueRitems = new List<RenderItem>();

        private PassConstants _mainPassCB;

        private Vector3 _eyePos;
        private Matrix _proj = Matrix.Identity;
        private Matrix _view = Matrix.Identity;

        private float _theta = 1.5f * MathUtil.Pi;
        private float _phi = 0.2f * MathUtil.Pi;
        private float _radius = 15.0f;

        private Point _lastMousePos;

        public TexColumnsApp(IntPtr hInstance) : base(hInstance)
        {
        }

        private FrameResource CurrFrameResource => _frameResources[_currFrameResourceIndex];
        private AutoResetEvent CurrentFenceEvent => _fenceEvents[_currFrameResourceIndex];

        public override void Initialize()
        {
            base.Initialize();

            // Reset the command list to prep for initialization commands.
            CommandList.Reset(DirectCmdListAlloc, null);

            LoadTextures();
            BuildRootSignature();
            BuildDescriptorHeaps();
            BuildShadersAndInputLayout();
            BuildShapeGeometry();            
            BuildMaterials();
            BuildRenderItems();
            BuildFrameResources();
            BuildPSOs();

            // Execute the initialization commands.
            CommandList.Close();
            CommandQueue.ExecuteCommandList(CommandList);

            // Wait until initialization is complete.
            FlushCommandQueue();
        }

        protected override void OnResize()
        {
            base.OnResize();

            // The window resized, so update the aspect ratio and recompute the projection matrix.
            _proj = Matrix.PerspectiveFovLH(0.25f * MathUtil.Pi, AspectRatio, 1.0f, 1000.0f);
        }

        protected override void Update(GameTimer gt)
        {
            UpdateCamera();

            // Cycle through the circular frame resource array.
            _currFrameResourceIndex = (_currFrameResourceIndex + 1) % NumFrameResources;

            // Has the GPU finished processing the commands of the current frame resource?
            // If not, wait until the GPU has completed commands up to this fence point.
            if (CurrFrameResource.Fence != 0 && Fence.CompletedValue < CurrFrameResource.Fence)
            {
                Fence.SetEventOnCompletion(CurrFrameResource.Fence, CurrentFenceEvent.SafeWaitHandle.DangerousGetHandle());
                CurrentFenceEvent.WaitOne();
            }

            UpdateObjectCBs();
            UpdateMaterialCBs();
            UpdateMainPassCB(gt);
        }

        protected override void Draw(GameTimer gt)
        {
            CommandAllocator cmdListAlloc = CurrFrameResource.CmdListAlloc;

            // Reuse the memory associated with command recording.
            // We can only reset when the associated command lists have finished execution on the GPU.
            cmdListAlloc.Reset();

            // A command list can be reset after it has been added to the command queue via ExecuteCommandList.
            // Reusing the command list reuses memory.
            CommandList.Reset(cmdListAlloc, _opaquePso);

            CommandList.SetViewport(Viewport);
            CommandList.SetScissorRectangles(ScissorRectangle);

            // Indicate a state transition on the resource usage.
            CommandList.ResourceBarrierTransition(CurrentBackBuffer, ResourceStates.Present, ResourceStates.RenderTarget);

            // Clear the back buffer and depth buffer.
            CommandList.ClearRenderTargetView(CurrentBackBufferView, Color.LightSteelBlue);
            CommandList.ClearDepthStencilView(CurrentDepthStencilView, ClearFlags.FlagsDepth | ClearFlags.FlagsStencil, 1.0f, 0);

            // Specify the buffers we are going to render to.            
            CommandList.SetRenderTargets(CurrentBackBufferView, CurrentDepthStencilView);

            CommandList.SetDescriptorHeaps(_descriptorHeaps.Length, _descriptorHeaps);

            CommandList.SetGraphicsRootSignature(_rootSignature);

            Resource passCB = CurrFrameResource.PassCB.Resource;
            CommandList.SetGraphicsRootConstantBufferView(2, passCB.GPUVirtualAddress);

            DrawRenderItems(CommandList, _opaqueRitems);

            // Indicate a state transition on the resource usage.
            CommandList.ResourceBarrierTransition(CurrentBackBuffer, ResourceStates.RenderTarget, ResourceStates.Present);

            // Done recording commands.
            CommandList.Close();

            // Add the command list to the queue for execution.
            CommandQueue.ExecuteCommandList(CommandList);

            // Present the buffer to the screen. Presenting will automatically swap the back and front buffers.
            SwapChain.Present(0, PresentFlags.None);

            // Advance the fence value to mark commands up to this fence point.
            CurrFrameResource.Fence = ++CurrentFence;

            // Add an instruction to the command queue to set a new fence point. 
            // Because we are on the GPU timeline, the new fence point won't be 
            // set until the GPU finishes processing all the commands prior to this Signal().
            CommandQueue.Signal(Fence, CurrentFence);
        }

        protected override void OnMouseDown(MouseButtons button, Point location)
        {
            base.OnMouseDown(button, location);
            _lastMousePos = location;            
        }

        protected override void OnMouseMove(MouseButtons button, Point location)
        {
            if ((button & MouseButtons.Left) != 0)
            {
                // Make each pixel correspond to a quarter of a degree.                
                float dx = MathUtil.DegreesToRadians(0.25f * (location.X - _lastMousePos.X));
                float dy = MathUtil.DegreesToRadians(0.25f * (location.Y - _lastMousePos.Y));

                // Update angles based on input to orbit camera around box.
                _theta += dx;
                _phi += dy;

                // Restrict the angle mPhi.
                _phi = MathUtil.Clamp(_phi, 0.1f, MathUtil.Pi - 0.1f);
            }
            else if ((button & MouseButtons.Right) != 0)
            {
                // Make each pixel correspond to a quarter of a degree.                
                float dx = 0.05f * (location.X - _lastMousePos.X);
                float dy = 0.05f * (location.Y - _lastMousePos.Y);

                // Update the camera radius based on input.
                _radius += dx - dy;

                // Restrict the radius.
                _radius = MathUtil.Clamp(_radius, 5.0f, 150.0f);
            }

            _lastMousePos = location;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (Texture texture in _textures.Values) texture.Dispose();
                foreach (FrameResource frameResource in _frameResources) frameResource.Dispose();
                _rootSignature.Dispose();
                foreach (MeshGeometry geometry in _geometries.Values) geometry.Dispose();                
                _opaquePso.Dispose();
            }
            base.Dispose(disposing);
        }

        private void UpdateCamera()
        {
            // Convert Spherical to Cartesian coordinates.
            _eyePos.X = _radius * MathHelper.Sinf(_phi) * MathHelper.Cosf(_theta);
            _eyePos.Z = _radius * MathHelper.Sinf(_phi) * MathHelper.Sinf(_theta);
            _eyePos.Y = _radius * MathHelper.Cosf(_phi);

            // Build the view matrix.
            _view = Matrix.LookAtLH(_eyePos, Vector3.Zero, Vector3.Up);
        }

        private void UpdateObjectCBs()
        {
            foreach (RenderItem e in _allRitems)
            {
                // Only update the cbuffer data if the constants have changed.  
                // This needs to be tracked per frame resource. 
                if (e.NumFramesDirty > 0)
                {
                    var objConstants = new ObjectConstants
                    {
                        World = Matrix.Transpose(e.World),
                        TexTransform = Matrix.Transpose(e.TexTransform)
                    };
                    CurrFrameResource.ObjectCB.CopyData(e.ObjCBIndex, ref objConstants);

                    // Next FrameResource need to be updated too.
                    e.NumFramesDirty--;
                }
            }
        }

        private void UpdateMaterialCBs()
        {
            UploadBuffer<MaterialConstants> currMaterialCB = CurrFrameResource.MaterialCB;
            foreach (Material mat in _materials.Values)
            {
                // Only update the cbuffer data if the constants have changed. If the cbuffer
                // data changes, it needs to be updated for each FrameResource.
                if (mat.NumFramesDirty > 0)
                {
                    var matConstants = new MaterialConstants
                    {
                        DiffuseAlbedo = mat.DiffuseAlbedo,
                        FresnelR0 = mat.FresnelR0,
                        Roughness = mat.Roughness,
                        MatTransform = Matrix.Transpose(mat.MatTransform)
                    };

                    currMaterialCB.CopyData(mat.MatCBIndex, ref matConstants);

                    // Next FrameResource need to be updated too.
                    mat.NumFramesDirty--;
                }
            }
        }

        private void UpdateMainPassCB(GameTimer gt)
        {
            Matrix viewProj = _view * _proj;
            Matrix invView = Matrix.Invert(_view);
            Matrix invProj = Matrix.Invert(_proj);
            Matrix invViewProj = Matrix.Invert(viewProj);

            _mainPassCB.View = Matrix.Transpose(_view);
            _mainPassCB.InvView = Matrix.Transpose(invView);
            _mainPassCB.Proj = Matrix.Transpose(_proj);
            _mainPassCB.InvProj = Matrix.Transpose(invProj);
            _mainPassCB.ViewProj = Matrix.Transpose(viewProj);
            _mainPassCB.InvViewProj = Matrix.Transpose(invViewProj);
            _mainPassCB.EyePosW = _eyePos;
            _mainPassCB.RenderTargetSize = new Vector2(ClientWidth, ClientHeight);
            _mainPassCB.InvRenderTargetSize = 1.0f / _mainPassCB.RenderTargetSize;
            _mainPassCB.NearZ = 1.0f;
            _mainPassCB.FarZ = 1000.0f;
            _mainPassCB.TotalTime = gt.TotalTime;
            _mainPassCB.DeltaTime = gt.DeltaTime;
            _mainPassCB.AmbientLight = new Vector4(0.25f, 0.25f, 0.35f, 1.0f);
            _mainPassCB.Lights.Light1.Direction = new Vector3(0.57735f, -0.57735f, 0.57735f);
            _mainPassCB.Lights.Light1.Strength = new Vector3(0.6f);
            _mainPassCB.Lights.Light2.Direction = new Vector3(-0.57735f, -0.57735f, 0.57735f);
            _mainPassCB.Lights.Light2.Strength = new Vector3(0.3f);
            _mainPassCB.Lights.Light3.Direction = new Vector3(0.0f, -0.707f, -0.707f);
            _mainPassCB.Lights.Light3.Strength = new Vector3(0.15f);

            CurrFrameResource.PassCB.CopyData(0, ref _mainPassCB);
        }

        private void LoadTextures()
        {
            AddTexture("bricksTex", "bricks.dds");
            AddTexture("stoneTex", "stone.dds");
            AddTexture("tileTex", "tile.dds");
        }

        private void AddTexture(string name, string filename)
        {
            var tex = new Texture
            {
                Name = name,
                Filename = $"Textures\\{filename}"
            };
            tex.Resource = TextureUtilities.CreateTextureFromDDS(D3DDevice, tex.Filename);
            _textures[tex.Name] = tex;
        }

        private void BuildRootSignature()
        {
            var texTable = new DescriptorRange(DescriptorRangeType.ShaderResourceView, 1, 0);

            var descriptor1 = new RootDescriptor(0, 0);
            var descriptor2 = new RootDescriptor(1, 0);
            var descriptor3 = new RootDescriptor(2, 0);

            // Root parameter can be a table, root descriptor or root constants.
            // Perfomance TIP: Order from most frequent to least frequent.
            var slotRootParameters = new[]
            {
                new RootParameter(ShaderVisibility.Pixel, texTable),
                new RootParameter(ShaderVisibility.Vertex, descriptor1, RootParameterType.ConstantBufferView),
                new RootParameter(ShaderVisibility.All, descriptor2, RootParameterType.ConstantBufferView),
                new RootParameter(ShaderVisibility.All, descriptor3, RootParameterType.ConstantBufferView)
            };

            // A root signature is an array of root parameters.
            var rootSigDesc = new RootSignatureDescription(
                RootSignatureFlags.AllowInputAssemblerInputLayout,
                slotRootParameters,
                GetStaticSamplers());

            _rootSignature = D3DDevice.CreateRootSignature(rootSigDesc.Serialize());
        }

        private void BuildDescriptorHeaps()
        {
            //
            // Create the SRV heap.
            //
            var srvHeapDesc = new DescriptorHeapDescription
            {
                DescriptorCount = 3,
                Type = DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView,
                Flags = DescriptorHeapFlags.ShaderVisible
            };
            _srvDescriptorHeap = D3DDevice.CreateDescriptorHeap(srvHeapDesc);
            _descriptorHeaps = new[] { _srvDescriptorHeap };

            //
            // Fill out the heap with actual descriptors.
            //
            CpuDescriptorHandle hDescriptor = _srvDescriptorHeap.CPUDescriptorHandleForHeapStart;

            Resource bricksTex = _textures["bricksTex"].Resource;
            Resource stoneTex = _textures["stoneTex"].Resource;
            Resource tileTex = _textures["tileTex"].Resource;

            // Ref: http://www.notjustcode.it/Blog/RenderTarget_DX12
            const int DefaultShader4ComponentMapping = 5768;
            var srvDesc = new ShaderResourceViewDescription
            {
                Shader4ComponentMapping = DefaultShader4ComponentMapping,
                Format = bricksTex.Description.Format,
                Dimension = ShaderResourceViewDimension.Texture2D,
                Texture2D = new ShaderResourceViewDescription.Texture2DResource
                {
                    MostDetailedMip = 0,
                    MipLevels = -1,
                    ResourceMinLODClamp = 0.0f
                }
            };

            D3DDevice.CreateShaderResourceView(bricksTex, srvDesc, hDescriptor);

            // Next descriptor.
            hDescriptor += CbvSrvUavDescriptorSize;

            srvDesc.Format = stoneTex.Description.Format;
            D3DDevice.CreateShaderResourceView(stoneTex, srvDesc, hDescriptor);

            // Next descriptor.
            hDescriptor += CbvSrvUavDescriptorSize;

            srvDesc.Format = tileTex.Description.Format;
            D3DDevice.CreateShaderResourceView(tileTex, srvDesc, hDescriptor);
        }

        private void BuildShadersAndInputLayout()
        {
            _shaders["standardVS"] = D3DUtil.CompileShader("Shaders\\Default.hlsl", "VS", "vs_5_0");
            _shaders["opaquePS"] = D3DUtil.CompileShader("Shaders\\Default.hlsl", "PS", "ps_5_0");

            _inputLayout = new InputLayoutDescription(new[]
            {
                new InputElement("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                new InputElement("NORMAL", 0, Format.R32G32B32_Float, 12, 0),
                new InputElement("TEXCOORD", 0, Format.R32G32_Float, 24, 0)
            });
        }

        private void BuildShapeGeometry()
        {
            GeometryGenerator.MeshData box = GeometryGenerator.CreateBox(1.0f, 1.0f, 1.0f, 3);
            GeometryGenerator.MeshData grid = GeometryGenerator.CreateGrid(20.0f, 30.0f, 60, 40);
            GeometryGenerator.MeshData sphere = GeometryGenerator.CreateSphere(0.5f, 20, 20);
            GeometryGenerator.MeshData cylinder = GeometryGenerator.CreateCylinder(0.5f, 0.3f, 3.0f, 20, 20);

            //
            // We are concatenating all the geometry into one big vertex/index buffer. So
            // define the regions in the buffer each submesh covers.
            //

            // Cache the vertex offsets to each object in the concatenated vertex buffer.
            int boxVertexOffset = 0;
            int gridVertexOffset = box.Vertices.Count;
            int sphereVertexOffset = gridVertexOffset + grid.Vertices.Count;
            int cylinderVertexOffset = sphereVertexOffset + sphere.Vertices.Count;

            // Cache the starting index for each object in the concatenated index buffer.
            int boxIndexOffset = 0;
            int gridIndexOffset = box.Indices32.Count;
            int sphereIndexOffset = gridIndexOffset + grid.Indices32.Count;
            int cylinderIndexOffset = sphereIndexOffset + sphere.Indices32.Count;

            // Define the SubmeshGeometry that cover different 
            // regions of the vertex/index buffers.

            var boxSubmesh = new SubmeshGeometry
            {
                IndexCount = box.Indices32.Count,
                StartIndexLocation = boxIndexOffset,
                BaseVertexLocation = boxVertexOffset
            };

            var gridSubmesh = new SubmeshGeometry
            {
                IndexCount = grid.Indices32.Count,
                StartIndexLocation = gridIndexOffset,
                BaseVertexLocation = gridVertexOffset
            };

            var sphereSubmesh = new SubmeshGeometry
            {
                IndexCount = sphere.Indices32.Count,
                StartIndexLocation = sphereIndexOffset,
                BaseVertexLocation = sphereVertexOffset
            };

            var cylinderSubmesh = new SubmeshGeometry
            {
                IndexCount = cylinder.Indices32.Count,
                StartIndexLocation = cylinderIndexOffset,
                BaseVertexLocation = cylinderVertexOffset
            };

            //
            // Extract the vertex elements we are interested in and pack the
            // vertices of all the meshes into one vertex buffer.
            //

            int totalVertexCount =
                box.Vertices.Count +
                grid.Vertices.Count +
                sphere.Vertices.Count +
                cylinder.Vertices.Count;

            var vertices = new Vertex[totalVertexCount];

            int k = 0;
            for (int i = 0; i < box.Vertices.Count; ++i, ++k)
            {
                vertices[k].Pos = box.Vertices[i].Position;
                vertices[k].Normal = box.Vertices[i].Normal;
                vertices[k].TexC = box.Vertices[i].TexC;
            }

            for (int i = 0; i < grid.Vertices.Count; ++i, ++k)
            {
                vertices[k].Pos = grid.Vertices[i].Position;
                vertices[k].Normal = grid.Vertices[i].Normal;
                vertices[k].TexC = grid.Vertices[i].TexC;
            }

            for (int i = 0; i < sphere.Vertices.Count; ++i, ++k)
            {
                vertices[k].Pos = sphere.Vertices[i].Position;
                vertices[k].Normal = sphere.Vertices[i].Normal;
                vertices[k].TexC = sphere.Vertices[i].TexC;
            }

            for (int i = 0; i < cylinder.Vertices.Count; ++i, ++k)
            {
                vertices[k].Pos = cylinder.Vertices[i].Position;
                vertices[k].Normal = cylinder.Vertices[i].Normal;
                vertices[k].TexC = cylinder.Vertices[i].TexC;
            }

            var indices = new List<short>();
            indices.AddRange(box.GetIndices16());
            indices.AddRange(grid.GetIndices16());
            indices.AddRange(sphere.GetIndices16());
            indices.AddRange(cylinder.GetIndices16());

            var geo = MeshGeometry.New(D3DDevice, CommandList, vertices, indices.ToArray(), "shapeGeo");

            geo.DrawArgs["box"] = boxSubmesh;
            geo.DrawArgs["grid"] = gridSubmesh;
            geo.DrawArgs["sphere"] = sphereSubmesh;
            geo.DrawArgs["cylinder"] = cylinderSubmesh;

            _geometries[geo.Name] = geo;
        }

        private void BuildPSOs()
        {
            //
            // PSO for opaque objects.
            //

            var opaquePsoDesc = new GraphicsPipelineStateDescription
            {
                InputLayout = _inputLayout,
                RootSignature = _rootSignature,
                VertexShader = _shaders["standardVS"],
                PixelShader = _shaders["opaquePS"],
                RasterizerState = RasterizerStateDescription.Default(),
                BlendState = BlendStateDescription.Default(),
                DepthStencilState = DepthStencilStateDescription.Default(),
                SampleMask = int.MaxValue,
                PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
                RenderTargetCount = 1,
                SampleDescription = new SampleDescription(MsaaCount, MsaaQuality),
                DepthStencilFormat = DepthStencilFormat
            };
            opaquePsoDesc.RenderTargetFormats[0] = BackBufferFormat;

            _opaquePso = D3DDevice.CreateGraphicsPipelineState(opaquePsoDesc);
        }

        private void BuildFrameResources()
        {
            for (int i = 0; i < NumFrameResources; i++)
            {
                _frameResources.Add(new FrameResource(D3DDevice, 1, _allRitems.Count, _materials.Count));
                _fenceEvents.Add(new AutoResetEvent(false));
            }
        }

        private void BuildMaterials()
        {
            _materials["bricks0"] = new Material
            {
                Name = "bricks0",
                MatCBIndex = 0,
                DiffuseSrvHeapIndex = 0,
                DiffuseAlbedo = Color.ForestGreen.ToVector4(),
                FresnelR0 = new Vector3(0.02f),
                Roughness = 0.1f
            };

            _materials["stone0"] = new Material
            {
                Name = "stone0",
                MatCBIndex = 1,
                DiffuseSrvHeapIndex = 1,
                DiffuseAlbedo = Color.LightSteelBlue.ToVector4(),
                FresnelR0 = new Vector3(0.05f),
                Roughness = 0.3f
            };

            _materials["tile0"] = new Material
            {
                Name = "tile0",
                MatCBIndex = 2,
                DiffuseSrvHeapIndex = 2,
                DiffuseAlbedo = Color.LightGray.ToVector4(),
                FresnelR0 = new Vector3(0.02f),
                Roughness = 0.2f
            };
        }

        private void BuildRenderItems()
        {
            var boxRitem = new RenderItem();
            boxRitem.World = Matrix.Scaling(2.0f, 2.0f, 2.0f) * Matrix.Translation(0.0f, 0.5f, 0.0f);
            boxRitem.ObjCBIndex = 0;
            boxRitem.Mat = _materials["stone0"];
            boxRitem.Geo = _geometries["shapeGeo"];
            boxRitem.PrimitiveType = PrimitiveTopology.TriangleList;
            boxRitem.IndexCount = boxRitem.Geo.DrawArgs["box"].IndexCount;
            boxRitem.StartIndexLocation = boxRitem.Geo.DrawArgs["box"].StartIndexLocation;
            boxRitem.BaseVertexLocation = boxRitem.Geo.DrawArgs["box"].BaseVertexLocation;
            _allRitems.Add(boxRitem);

            var gridRitem = new RenderItem();
            gridRitem.World = Matrix.Identity;
            gridRitem.ObjCBIndex = 1;
            gridRitem.Mat = _materials["tile0"];
            gridRitem.Geo = _geometries["shapeGeo"];
            gridRitem.PrimitiveType = PrimitiveTopology.TriangleList;
            gridRitem.IndexCount = gridRitem.Geo.DrawArgs["grid"].IndexCount;
            gridRitem.StartIndexLocation = gridRitem.Geo.DrawArgs["grid"].StartIndexLocation;
            gridRitem.BaseVertexLocation = gridRitem.Geo.DrawArgs["grid"].BaseVertexLocation;
            _allRitems.Add(gridRitem);

            int objCBIndex = 2;
            for (int i = 0; i < 5; ++i)
            {
                var leftCylRitem = new RenderItem();
                var rightCylRitem = new RenderItem();
                var leftSphereRitem = new RenderItem();
                var rightSphereRitem = new RenderItem();

                leftCylRitem.World = Matrix.Translation(-5.0f, 1.5f, -10.0f + i * 5.0f);
                leftCylRitem.ObjCBIndex = objCBIndex++;
                leftCylRitem.Mat = _materials["bricks0"];
                leftCylRitem.Geo = _geometries["shapeGeo"];
                leftCylRitem.PrimitiveType = PrimitiveTopology.TriangleList;
                leftCylRitem.IndexCount = leftCylRitem.Geo.DrawArgs["cylinder"].IndexCount;
                leftCylRitem.StartIndexLocation = leftCylRitem.Geo.DrawArgs["cylinder"].StartIndexLocation;
                leftCylRitem.BaseVertexLocation = leftCylRitem.Geo.DrawArgs["cylinder"].BaseVertexLocation;

                rightCylRitem.World = Matrix.Translation(+5.0f, 1.5f, -10.0f + i * 5.0f);
                rightCylRitem.ObjCBIndex = objCBIndex++;
                rightCylRitem.Mat = _materials["bricks0"];
                rightCylRitem.Geo = _geometries["shapeGeo"];
                rightCylRitem.PrimitiveType = PrimitiveTopology.TriangleList;
                rightCylRitem.IndexCount = rightCylRitem.Geo.DrawArgs["cylinder"].IndexCount;
                rightCylRitem.StartIndexLocation = rightCylRitem.Geo.DrawArgs["cylinder"].StartIndexLocation;
                rightCylRitem.BaseVertexLocation = rightCylRitem.Geo.DrawArgs["cylinder"].BaseVertexLocation;

                leftSphereRitem.World = Matrix.Translation(-5.0f, 3.5f, -10.0f + i * 5.0f);
                leftSphereRitem.ObjCBIndex = objCBIndex++;
                leftSphereRitem.Mat = _materials["stone0"];
                leftSphereRitem.Geo = _geometries["shapeGeo"];
                leftSphereRitem.PrimitiveType = PrimitiveTopology.TriangleList;
                leftSphereRitem.IndexCount = leftSphereRitem.Geo.DrawArgs["sphere"].IndexCount;
                leftSphereRitem.StartIndexLocation = leftSphereRitem.Geo.DrawArgs["sphere"].StartIndexLocation;
                leftSphereRitem.BaseVertexLocation = leftSphereRitem.Geo.DrawArgs["sphere"].BaseVertexLocation;

                rightSphereRitem.World = Matrix.Translation(+5.0f, 3.5f, -10.0f + i * 5.0f);
                rightSphereRitem.ObjCBIndex = objCBIndex++;
                rightSphereRitem.Mat = _materials["stone0"];
                rightSphereRitem.Geo = _geometries["shapeGeo"];
                rightSphereRitem.PrimitiveType = PrimitiveTopology.TriangleList;
                rightSphereRitem.IndexCount = rightSphereRitem.Geo.DrawArgs["sphere"].IndexCount;
                rightSphereRitem.StartIndexLocation = rightSphereRitem.Geo.DrawArgs["sphere"].StartIndexLocation;
                rightSphereRitem.BaseVertexLocation = rightSphereRitem.Geo.DrawArgs["sphere"].BaseVertexLocation;

                _allRitems.Add(leftCylRitem);
                _allRitems.Add(rightCylRitem);
                _allRitems.Add(leftSphereRitem);
                _allRitems.Add(rightSphereRitem);
            }

            // All the render items are opaque.
            _opaqueRitems.AddRange(_allRitems);
        }

        private void DrawRenderItems(GraphicsCommandList cmdList, List<RenderItem> ritems)
        {
            int objCBByteSize = D3DUtil.CalcConstantBufferByteSize<ObjectConstants>();
            int matCBByteSize = D3DUtil.CalcConstantBufferByteSize<MaterialConstants>();

            Resource objectCB = CurrFrameResource.ObjectCB.Resource;
            Resource matCB = CurrFrameResource.MaterialCB.Resource;

            foreach (RenderItem ri in ritems)
            {
                cmdList.SetVertexBuffer(0, ri.Geo.VertexBufferView);
                cmdList.SetIndexBuffer(ri.Geo.IndexBufferView);
                cmdList.PrimitiveTopology = ri.PrimitiveType;

                GpuDescriptorHandle tex = _srvDescriptorHeap.GPUDescriptorHandleForHeapStart + ri.Mat.DiffuseSrvHeapIndex * CbvSrvUavDescriptorSize;

                long objCBAddress = objectCB.GPUVirtualAddress + ri.ObjCBIndex * objCBByteSize;
                long matCBAddress = matCB.GPUVirtualAddress + ri.Mat.MatCBIndex * matCBByteSize;

                cmdList.SetGraphicsRootDescriptorTable(0, tex);
                cmdList.SetGraphicsRootConstantBufferView(1, objCBAddress);
                cmdList.SetGraphicsRootConstantBufferView(3, matCBAddress);

                cmdList.DrawIndexedInstanced(ri.IndexCount, 1, ri.StartIndexLocation, ri.BaseVertexLocation, 0);
            }
        }

        // Applications usually only need a handful of samplers. So just define them all up front
        // and keep them available as part of the root signature.
        private static StaticSamplerDescription[] GetStaticSamplers() => new[]
        {
            // PointWrap
            new StaticSamplerDescription(ShaderVisibility.Pixel, 0, 0)
            {
                Filter = Filter.MinMagMipPoint,
                AddressU = TextureAddressMode.Wrap,
                AddressV = TextureAddressMode.Wrap,
                AddressW = TextureAddressMode.Wrap
            },
            // PointClamp
            new StaticSamplerDescription(ShaderVisibility.Pixel, 1, 0)
            {
                Filter = Filter.MinMagMipPoint,
                AddressU = TextureAddressMode.Clamp,
                AddressV = TextureAddressMode.Clamp,
                AddressW = TextureAddressMode.Clamp
            },
            // LinearWrap
            new StaticSamplerDescription(ShaderVisibility.Pixel, 2, 0)
            {
                Filter = Filter.MinMagMipLinear,
                AddressU = TextureAddressMode.Wrap,
                AddressV = TextureAddressMode.Wrap,
                AddressW = TextureAddressMode.Wrap
            },
            // LinearClamp
            new StaticSamplerDescription(ShaderVisibility.Pixel, 3, 0)
            {
                Filter = Filter.MinMagMipLinear,
                AddressU = TextureAddressMode.Clamp,
                AddressV = TextureAddressMode.Clamp,
                AddressW = TextureAddressMode.Clamp
            },
            // AnisotropicWrap
            new StaticSamplerDescription(ShaderVisibility.Pixel, 4, 0)
            {
                Filter = Filter.Anisotropic,
                AddressU = TextureAddressMode.Wrap,
                AddressV = TextureAddressMode.Wrap,
                AddressW = TextureAddressMode.Wrap
            },
            // AnisotropicClamp
            new StaticSamplerDescription(ShaderVisibility.Pixel, 5, 0)
            {
                Filter = Filter.Anisotropic,
                AddressU = TextureAddressMode.Clamp,
                AddressV = TextureAddressMode.Clamp,
                AddressW = TextureAddressMode.Clamp
            }
        };
    }
}
