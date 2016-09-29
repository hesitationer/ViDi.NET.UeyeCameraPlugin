using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.ComponentModel;
using uEye;
using System.Threading;
using System.Collections.Specialized;
using System.Globalization;
using ViDi2.Camera;

namespace ViDi2.Training
{
    class UeyeCameraCapabilities : ICameraCapabilities
    {

    public bool CanGrabSingle
    {
	    get { return true; }
    }
    
    public bool CanGrabContinuous
    {
        get { return true; }
    }

    public bool CanSaveParametersToFile 
    {
        get { return true; }
    }

    public bool CanSaveParametersToDevice
    {
        get { return true; }
    }
}


    class UEyeCamera : ICamera, INotifyPropertyChanged
    {
        private uEye.Camera Camera = null;

        public event ImageGrabbedHandler ImageGrabbed;

        public bool IsGrabbingContinuous
        {
            get
            {
                return bLive;
            }
            internal set
            {
                bLive = value;
                RaisePropertyChanged("IsLive");
                RaisePropertyChanged("IsIdle");
            }
        }

        bool bLive = false;

        public string Name
        {
            get;
            private set;
        }
        public bool IsOpen
        {
            get
            {
                return (Camera != null && Camera.IsOpened);
            }
        }

        int cameraIdx;
        public UEyeCamera(int idx, string name, ViDi2.Training.UI.UEyeCameraProvider provider )
        {
            Camera = new uEye.Camera();
            cameraIdx = idx;
            Provider = provider;
            Name = name;
            parameters = new List<ICameraParameter>
            {
                new CameraParameter("Grayscale", () => GrayScale, null),
                new CameraParameter("Sensor Size", () => 
                    {
                        uEye.Types.SensorInfo info;
                        Camera.Information.GetSensorInfo(out info);
                        return new System.Windows.Size(info.MaxSize.Width, info.MaxSize.Height);
                    }, 
                    null),
                new CameraParameter("Exposure Time", () => ExposureTime, (value) => ExposureTime = (double)value),
                new CameraParameter("Frame Rate", () => FrameRate, (value) => FrameRate = (double)value),
                new CameraParameter("Binning", () => Binning, (value) => Binning = (Point)value),
                new CameraParameter("AOI Offset", () => AOIOffset, (value) => AOIOffset = (Point)value),
                new CameraParameter("AOI Size", () => AOISize, (value) => AOISize = (Point)value),
                new CameraParameter("Pixel Clock", () => PixelClock, (value) => PixelClock = (int)value),
                new CameraParameter("Managed Image", () => ManagedImages, (value) => ManagedImages = (bool)value),
            
            };
        }

        private IImage MemoryToImage(int idx)
        {
            uEye.Defines.ColorMode mode;
            Camera.PixelFormat.Get(out mode);
            ImageChannelDepth depth = ImageChannelDepth.Depth8;
            int channels = 0;

            if (mode == uEye.Defines.ColorMode.Mono8)
            {
                channels = 1;
                depth = ImageChannelDepth.Depth8;
            }
            else if (mode == uEye.Defines.ColorMode.BGR8Packed || mode == uEye.Defines.ColorMode.RGB8Packed)
            {
                channels = 3;
                depth = ImageChannelDepth.Depth8;

            }
            else if (mode == uEye.Defines.ColorMode.BGRA8Packed)
            {
                channels = 3;
                depth = ImageChannelDepth.Depth8;
            }
            else
            {
                throw new Exception("unknown image format");
            }
          
            int width;
            int height;
            int pitch;
            //IntPtr img;
            //ueyeCamera.Memory.ToIntPtr(idx, out img);
            Camera.Memory.GetHeight(idx, out height);
            Camera.Memory.GetWidth(idx, out width);
            uEye.Types.Size<int> s = new uEye.Types.Size<int>();
            Camera.Memory.GetSize(idx,out s );
          
            Camera.Memory.GetPitch(idx, out pitch);

            IntPtr ptr = new IntPtr();
            IImage img = null;
            Camera.Memory.ToIntPtr(idx, out ptr);

            if (ManagedImages)
            {      
                Byte[] u8img;
                Camera.Memory.CopyToArray(idx, out u8img);
                Camera.Memory.Free(idx);
                Camera.Memory.Allocate(s);
                img = new ByteImage(width, height, channels, depth, u8img, pitch);
            }
            else
                img = new RawImage(width, height, channels, depth, ptr, pitch);

            return img;

        }


        public void Open()
        {

            if (Camera.IsOpened)
                return;

            uEye.Defines.Status statusRet = 0;

            // Open Camera
            statusRet = Camera.Init(cameraIdx);
            if (statusRet != uEye.Defines.Status.Success)
            {
                throw new Exception("Camera initializing failed");
            }
            uEye.Types.SensorInfo info = new uEye.Types.SensorInfo();
            Camera.Information.GetSensorInfo(out info);

           Camera.Parameter.Load();

            statusRet = Camera.Memory.Allocate();
            if (statusRet != uEye.Defines.Status.Success)
            {
                throw new Exception("Allocate Memory failed");

            }
            Camera.Trigger.Set(uEye.Defines.TriggerMode.Continuous);
            Camera.EventFrame += onFrameEvent;
            try
            {
                Camera.Device.Feature.ShutterMode.Set(uEye.Defines.Shuttermode.Global);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        public void Close()
        {
            if (!Camera.IsOpened)
                return;

            uEye.Defines.Status statusRet;
            statusRet = Camera.Exit();

            if (statusRet != uEye.Defines.Status.Success)
            {
                throw new Exception("Failed to close camera");
            }
        }

        public void StartGrabContinuous()
        {
            Camera.Trigger.Set(uEye.Defines.TriggerMode.Continuous);

            //if ( Camera.Acquisition.Freeze() == uEye.Defines.Status.Success)
            if (Camera.Acquisition.Capture() == uEye.Defines.Status.Success)
            {
                IsGrabbingContinuous = true;
            }
            else
            {
                throw new Exception("failed to start live");
            }
        }

        public void StopGrabContinuous()
        {
            if (bLive)
            {
                uEye.Defines.Status statusRet = 0;

                Camera.Acquisition.Stop();
                if (statusRet != uEye.Defines.Status.Success)
                    throw new Exception("failed to stop live");

                IsGrabbingContinuous = false;
            }
        }

        public void LoadParameters(string parameters_file)
        {
            uEye.Defines.Status statusRet = 0;

            Int32[] memList;
            statusRet = Camera.Memory.GetList(out memList);
            if (statusRet != uEye.Defines.Status.Success)
            {
                throw new Exception("Get memory list failed: " + statusRet);
            }

            statusRet = Camera.Memory.Free(memList);
            if (statusRet != uEye.Defines.Status.Success)
            {
                throw new Exception("Free memory list failed: " + statusRet);
            }

            statusRet = Camera.Parameter.Load(parameters_file);
            if (statusRet != uEye.Defines.Status.Success)
            {
                throw new Exception("Loading parameter failed: " + statusRet);
            }

            uEye.Types.Size<int> t = new uEye.Types.Size<int>();


            statusRet = Camera.Memory.Allocate();
            if (statusRet != uEye.Defines.Status.SUCCESS)
            {
                throw new Exception("Allocate Memory failed");
            }
        }

        public void SaveParameters(string parameters_file)
        {
            uEye.Defines.Status statusRet = 0;

            statusRet = Camera.Parameter.Save(parameters_file);
            if (statusRet != uEye.Defines.Status.SUCCESS)
            {
                throw new Exception("failed to save Parameters");
            }
        }


        public IImage GrabSingle()
        {
            if (bLive)
                throw new Exception("cannot grab when live");

            Camera.Trigger.Set(uEye.Defines.TriggerMode.Software);
            IsGrabbingContinuous = false;

            if (Camera.Acquisition.Freeze(200) != uEye.Defines.Status.Success)
                throw new Exception("failed to snapshot");

            bool isFinished;
            Camera.Acquisition.IsFinished(out isFinished);
            if (!isFinished)
                throw new Exception("acquisition not finished");

            Int32 s32MemID;
            Camera.Memory.GetActive(out s32MemID);
            Camera.Memory.Lock(s32MemID);
            // callback that converts the image from memory to bmp
            IImage image = MemoryToImage(s32MemID);
            // free the memory when the copy is done to 
            // let the camera reuse it for next frames
            Camera.Memory.Unlock(s32MemID);

            return image;
        }

        private void onFrameEvent(object sender, EventArgs e)
        {
            try
            {
                // Get the camera
                uEye.Camera Camera = sender as uEye.Camera;

                if (!bLive)
                    return;

                // lock the memory
                Int32 s32MemID;
                Camera.Memory.GetActive(out s32MemID);
                Camera.Memory.Lock(s32MemID);
                // callback that converts the image from memory to bmp
                IImage img = MemoryToImage(s32MemID);
                {
                    ImageGrabbed(this,img);
                }
                // free the memory when the copy is done to 
                // let the camera reuse it for next frames
                Camera.Memory.Unlock(s32MemID);
            }
            catch (TaskCanceledException)
            {
                (sender as uEye.Camera).Acquisition.Stop();
                IsGrabbingContinuous = false;
            }
        }

        public Double ExposureTime
        {
            get
            {
                uEye.Defines.Status statusRet;
                Double dValue;
                statusRet = Camera.Timing.Exposure.Get(out dValue);

                if (statusRet != uEye.Defines.Status.SUCCESS)
                {
                    throw new Exception("failed to get Frame Rate");
                }

                return dValue;
            }
            set
            {
                uEye.Defines.Status statusRet;
                statusRet = Camera.Timing.Exposure.Set(value);

                if (statusRet != uEye.Defines.Status.SUCCESS)
                {
                    throw new Exception("failed to set Exposure Time");
                }
                RaisePropertyChanged("ExposureTime");
            }
        }
        public Double FrameRate
        {
            get
            {
                uEye.Defines.Status statusRet;
                Double dValue;
                statusRet = Camera.Timing.Framerate.Get(out dValue);

                if (statusRet != uEye.Defines.Status.SUCCESS)
                {
                    throw new Exception("failed to get Frame Rate");
                }

                return dValue;

            }
            set
            {
                uEye.Defines.Status statusRet;
                statusRet = Camera.Timing.Framerate.Set(value);

                if (statusRet != uEye.Defines.Status.SUCCESS)
                {
                    throw new Exception("failed to set Frame Rate");
                }
            }
        }
        public Int32 PixelClock
        {
            get
            {
                uEye.Defines.Status statusRet;
                Int32 s32Value;
                statusRet = Camera.Timing.PixelClock.Get(out s32Value);

                if (statusRet != uEye.Defines.Status.SUCCESS)
                {
                    throw new Exception("failed to get pixel clock");
                }

                return s32Value;
            }
            set
            {
                uEye.Defines.Status statusRet;
                statusRet = Camera.Timing.PixelClock.Set(value);

                if (statusRet != uEye.Defines.Status.SUCCESS)
                {
                    throw new Exception("failed to set pixel clock");
                }

            }
        }


        public Point AOIOffset
        {
               get
               {
                   System.Drawing.Rectangle rect = new System.Drawing.Rectangle();

                   Camera.Size.AOI.Get(out rect);

                   Point pt = new Point(rect.X,rect.Y);

                   return pt;
                }


                set
                {
                    System.Drawing.Rectangle rect = new System.Drawing.Rectangle();
                    Camera.Size.AOI.Get(out rect);
                    rect.X = (int)value.X; rect.Y = (int)value.Y;

                    uEye.Defines.Status statusRet;
                    statusRet = Camera.Size.AOI.Set(rect);

                    Int32[] memList;
                    statusRet = Camera.Memory.GetList(out memList);
                    statusRet = Camera.Memory.Free(memList);
                    statusRet = Camera.Memory.Allocate();

                    RaisePropertyChanged("AOIOffset");
                    RaisePropertyChanged("AOISize");
                }
        }

        public Point AOISize
        {
            get
            {
                System.Drawing.Rectangle rect = new System.Drawing.Rectangle();

                Camera.Size.AOI.Get(out rect);

                Point pt = new Point(rect.Height, rect.Width);

                return pt;
            }


            set
            {
                System.Drawing.Rectangle rect = new System.Drawing.Rectangle();
                Camera.Size.AOI.Get(out rect);
                rect.Height = (int)value.X; rect.Width = (int)value.Y;
                uEye.Defines.Status statusRet;
                statusRet = Camera.Size.AOI.Set(rect);


                Int32[] memList;
                statusRet = Camera.Memory.GetList(out memList);
                statusRet = Camera.Memory.Free(memList);
                statusRet = Camera.Memory.Allocate();

                RaisePropertyChanged("AIOOffset");
                RaisePropertyChanged("AOISize");
            }
        }


        public Double Gain
        {
            get
            {

                return 0;
            }

            set
            {
                RaisePropertyChanged("Gain");
            }

        }

        public Point Binning
        {
            get
            {
                int h, v;
                Camera.Size.Binning.GetFactorHorizontal(out h);
                Camera.Size.Binning.GetFactorVertical(out v);
                Point p = new Point();
                p.X = h; p.Y = v;
                return p;

            }
            set
            {
                Point p = value;
                uEye.Defines.BinningMode mode;
                uEye.Defines.BinningMode modeX;
                uEye.Defines.BinningMode modeY;
                switch ((int)p.X)
                {
                    case 1:
                        modeX = uEye.Defines.BinningMode.Disable;
                        break;
                    case 2:
                        modeX = uEye.Defines.BinningMode.Horizontal2X;
                        break;

                    case 3:
                        modeX = uEye.Defines.BinningMode.Horizontal3X;
                        break;

                    case 4:
                        modeX = uEye.Defines.BinningMode.Horizontal4X;
                        break;
                    case 5:
                        modeX = uEye.Defines.BinningMode.Horizontal5X;
                        break;
                    case 6:
                        modeX = uEye.Defines.BinningMode.Horizontal6X;
                        break;
                    case 8:
                        modeX = uEye.Defines.BinningMode.Horizontal8X;
                        break;
                    case 16:
                        modeX = uEye.Defines.BinningMode.Horizontal16X;
                        break;
                    default:
                        modeX = uEye.Defines.BinningMode.Disable;
                        break;
                }
                if (!Camera.Size.Binning.IsSupported(modeX))
                {
                    modeX = uEye.Defines.BinningMode.Disable;
                }

                switch ((int)p.Y)
                {
                    case 1:
                        modeY = uEye.Defines.BinningMode.Disable;
                        break;
                    case 2:
                        modeY = uEye.Defines.BinningMode.Vertical2X;
                        break;
                    case 3:
                        modeY = uEye.Defines.BinningMode.Vertical3X;
                        break;

                    case 4:
                        modeY = uEye.Defines.BinningMode.Vertical4X;
                        break;
                    case 5:
                        modeY = uEye.Defines.BinningMode.Vertical5X;
                        break;
                    case 6:
                        modeY = uEye.Defines.BinningMode.Vertical6X;
                        break;
                    case 8:
                        modeY = uEye.Defines.BinningMode.Vertical8X;
                        break;
                    case 16:
                        modeY = uEye.Defines.BinningMode.Vertical16X;
                        break;
                    default:
                        modeY = uEye.Defines.BinningMode.Disable;
                        break;
                }
                if (!Camera.Size.Binning.IsSupported(modeY))
                {
                    modeY = uEye.Defines.BinningMode.Disable;
                }
                mode = modeY | modeX;
                uEye.Defines.Status statusRet;
                statusRet = Camera.Size.Binning.Set(mode);
                if (statusRet != uEye.Defines.Status.SUCCESS)
                {
                    modeX = modeY = uEye.Defines.BinningMode.Disable;
                }

                Int32[] memList;
                statusRet = Camera.Memory.GetList(out memList);
                statusRet = Camera.Memory.Free(memList);
                statusRet = Camera.Memory.Allocate();

                RaisePropertyChanged("Binning");
            }
        }

        bool managedImages = false;
        public bool ManagedImages
        {
            get { return managedImages; }
            set { managedImages = value;
            RaisePropertyChanged("ManagedImages");}
        }

        public void SaveParametersToDevice()
        {
            uEye.Defines.Status statusRet;
            statusRet = Camera.Parameter.Save();
            if (statusRet != uEye.Defines.Status.SUCCESS)
            {
                throw new Exception("failed to save parameters to memory");
            }
        }

        public bool GrayScale
        {
            get
            {
                uEye.Defines.Status statusRet;
                uEye.Types.SensorInfo info = new uEye.Types.SensorInfo();
                statusRet = Camera.Information.GetSensorInfo(out info);

                if (statusRet != uEye.Defines.Status.Success)
                    throw new Exception("failed to get sensor mode");

                if (info.SensorColorMode == uEye.Defines.SensorColorMode.Monochrome)
                    return true;
                else
                    return false;
            }
        }

        public ICameraProvider Provider
        {
            get;
            private set;
        }

        UeyeCameraCapabilities capabilities = new UeyeCameraCapabilities();

        public ICameraCapabilities Capabilities
        {
            get
            {
                return  capabilities;
            }
        }

        private void RaisePropertyChanged(string prop)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(prop));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public IEnumerable<ICameraParameter> Parameters
        {
            get { return parameters; }
        }

        List<ICameraParameter> parameters;
    
        ICameraProvider ICamera.Provider
        {
            get { throw new NotImplementedException(); }
        }
    }
}
