using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Drawing;
using ViDi2.Training;
using ViDi2.Camera;
using ViDi2;

using System.Reflection;
using System.Collections.ObjectModel;

namespace ViDi2.Training.UI
{
    /// <summary>
    /// Interaction logic for UEyeCamera.xaml
    /// </summary>
    public partial class UEyeCameraProvider : IPlugin, ICameraProvider
    {
        public string Name { get { return "IDS uEye"; } }
        string IPlugin.Description { get { return "Provides IDS uEye Camera support"; } }
        int IPlugin.Version { get { return 1; } }

        IPluginContext context;

        public UEyeCameraProvider() { }

        void DiscoverUeyeCameras()
        {
            uEye.Types.CameraInformation[] cameraList;
            uEye.Info.Camera.GetCameraList(out cameraList);
            
            int cameraIdx = 0;

            foreach (var info in cameraList)
            {
                ICamera camera = new UEyeCamera(cameraIdx, info.Model, this);
                
                cameras.Add(camera);
                cameraIdx++;
            }
        }

        public ReadOnlyCollection<ICamera> Discover()
        {
            foreach (var camera in cameras)
            {
                if (camera.IsOpen)
                {
                    camera.Close();
                }
            }

            cameras.Clear();
            DiscoverUeyeCameras();
            return Cameras;
        }

        List<ICamera> cameras = new List<ICamera>();
        public ReadOnlyCollection<ICamera> Cameras
        {
            get
            {
                return cameras.AsReadOnly();
            }
        }

        bool initialized = false;
        public void Initialize(IPluginContext context)
        {
            if (initialized)
                return;

            initialized = true;

            AppDomain.CurrentDomain.AssemblyResolve += (s, args) =>
            {
                String resourceName = new AssemblyName(args.Name).Name + ".dll";
                var test = Assembly.GetExecutingAssembly().GetManifestResourceNames();
                string fullname = Array.Find(Assembly.GetExecutingAssembly().GetManifestResourceNames(), (str) => { return str.EndsWith(resourceName); });
                if (fullname == null)
                    return null;

                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(test[0]))
                {
                    if (stream != null)
                    {
                        Byte[] assemblyData = new Byte[stream.Length];
                        stream.Read(assemblyData, 0, assemblyData.Length);
                        return Assembly.Load(assemblyData);
                    }
                    else
                        return null;
                }
            };

            this.context = context;
        }

        public void DeInitialize() { }
    }
}
