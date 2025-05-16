using Emgu.CV;
using Emgu.CV.Face;
using Emgu.CV.Structure;
using System.Drawing;
using Emgu.CV.Util;
using Dapper;
using GFAS.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Newtonsoft.Json;
using System.Collections;
using System.Data;
using Emgu.CV.CvEnum;
using Emgu.CV.Ocl;
using Emgu.CV.ImgHash;
using Org.BouncyCastle.Crypto.Macs;
using Emgu.CV.Dnn;

namespace GFAS.Controllers
{
    public class GeoController : Controller
    {
        private readonly IConfiguration configuration;
        private readonly TSUISLRFIDDBContext context;
        private readonly UserLoginDBContext context1;

        public GeoController(IConfiguration configuration,TSUISLRFIDDBContext context,UserLoginDBContext context1)
        {
            this.configuration = configuration;
            this.context = context;
            this.context1 = context1;
        }

      

        private string GetRFIDConnectionString()
        {
            return this.configuration.GetConnectionString("RFID");
        }


        [Authorize]
        public IActionResult GeoFencing()
        {
            var session = HttpContext.Request.Cookies["Session"];
            var userName = HttpContext.Request.Cookies["UserName"];



            

            if (string.IsNullOrEmpty(session) || string.IsNullOrEmpty(userName))
            {
                return RedirectToAction("Login", "User");
            }



            var data = GetLocations();

            var pno = session; 
            var currentDate = DateTime.Now.ToString("yyyy-MM-dd");

            string connectionString = GetRFIDConnectionString();

            string query = @"
        SELECT TOP 1 TRBDGDA_BD_Inout 
        FROM T_TRBDGDAT_EARS 
        WHERE TRBDGDA_BD_PNO = @Pno 
        AND TRBDGDA_BD_DATE = @CurrentDate ORDER By TRBDGDA_BD_TIME DESC";

            string inoutValue = "";

            using (var connection = new SqlConnection(connectionString))
            {
                inoutValue = connection.QuerySingleOrDefault<string>(query, new { Pno = pno, CurrentDate = currentDate })?.Trim();
            }

            ViewBag.InOut = inoutValue; 

           
            return View();
        }

        public IActionResult GetLocations()
        {
            var UserId = HttpContext.Request.Cookies["Session"];
            string connectionString = GetRFIDConnectionString();

            string query = @"SELECT ps.Worksite FROM TSUISLRFIDDB.DBO.App_Position_Worksite AS ps 
                     INNER JOIN TSUISLRFIDDB.DBO.App_Emp_position AS es ON es.position = ps.position 
                     WHERE es.Pno = @UserId";

            using (var connection = new SqlConnection(connectionString))
            {
               
                var worksiteNamesString = connection.QuerySingleOrDefault<string>(query, new { UserId });

                if (string.IsNullOrEmpty(worksiteNamesString))
                {
                    ViewBag.PolyData = new List<object>();
                    return View();
                }

                
                var worksiteNames = worksiteNamesString.Split(',').Select(w => w.Trim()).ToList();

               
                var formattedWorksites = worksiteNames
                    .Select(name => $"'{name.Replace("'", "''")}'") 
                    .ToList();

                string s = string.Join(",", formattedWorksites);

                string query2 = @$"SELECT Longitude, Latitude, Range FROM TSUISLRFIDDB.DBO.App_LocationMaster 
                           WHERE work_site IN ({s})";

                var locations = connection.Query(query2).Select(loc => new
                {
                    Latitude = (double)loc.Latitude,
                    Longitude = (double)loc.Longitude,
                    Range = loc.Range
                }).ToList();

                ViewBag.PolyData = locations;
                return View();
            }
        }





        public IActionResult Logout()
        {
            HttpContext.Session.Remove("Session");
            return RedirectToAction("Login", "User");

        }


        [HttpPost]
        public IActionResult AttendanceData([FromBody] AttendanceRequest model)
        {
            try
            {
                var UserId = HttpContext.Request.Cookies["Session"];
                var UserName = HttpContext.Request.Cookies["UserName"];
                if (string.IsNullOrEmpty(UserId))
                    return Json(new { success = false, message = "User session not found!" });

                string Pno = UserId;
                string Name = UserName;

                string storedImagePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/Images/", $"{Pno}-{Name}.jpg");
                string lastCapturedPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/Images/", $"{Pno}-Captured.jpg");
 
                if (!System.IO.File.Exists(storedImagePath) && !System.IO.File.Exists(lastCapturedPath))
                {
                    return Json(new { success = false, message = "No reference image found to verify face!" });
                }


                string tempCapturedPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/Images/", $"{Pno}-Captured-{DateTime.Now.Ticks}.jpg");
                SaveBase64ImageToFile(model.ImageData, tempCapturedPath);

                bool isFaceMatched = false;

                using (Bitmap tempCaptured = new Bitmap(tempCapturedPath))
                {
                    if (System.IO.File.Exists(storedImagePath))
                    {
                        using (Bitmap stored = new Bitmap(storedImagePath))
                        {
                            isFaceMatched = VerifyFace(tempCaptured, stored);
                        }
                    }

                   
                    if (!isFaceMatched && System.IO.File.Exists(lastCapturedPath))
                    {
                        using (Bitmap lastCaptured = new Bitmap(lastCapturedPath))
                        {
                            isFaceMatched = VerifyFace(tempCaptured, lastCaptured);
                        }
                    }
                }

                
                System.IO.File.Delete(tempCapturedPath);

                if (isFaceMatched)
                {
                    string currentDate = DateTime.Now.ToString("yyyy/MM/dd");
                    string currentTime = DateTime.Now.ToString("HH:mm");

                    if (model.Type == "Punch In")
                    {
                        
                        string newCapturedPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/Images/", $"{Pno}-Captured.jpg");
                        SaveBase64ImageToFile(model.ImageData, newCapturedPath);

                        StoreData(currentDate, currentTime, null, Pno);
                    }
                    else
                    {
                        StoreData(currentDate, null, currentTime, Pno);
                    }

                    return Json(new { success = true, message = "Attendance recorded successfully." });
                }
                else
                {
                    return Json(new { success = false, message = "Face does not match!" });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }


        //private bool VerifyFace(Bitmap captured, Bitmap stored)
        //{
        //    try
        //    {
        //        // Convert Bitmaps to grayscale Mats
        //        Mat matCaptured = BitmapToMat(captured);
        //        Mat matStored = BitmapToMat(stored);

        //        CvInvoke.CvtColor(matCaptured, matCaptured, Emgu.CV.CvEnum.ColorConversion.Bgr2Gray);
        //        CvInvoke.CvtColor(matStored, matStored, Emgu.CV.CvEnum.ColorConversion.Bgr2Gray);

        //        // Load Haar cascade
        //        string cascadePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/Cascades/haarcascade_frontalface_default.xml");
        //        if (!System.IO.File.Exists(cascadePath))
        //        {
        //            Console.WriteLine("Error: Haarcascade file not found!");
        //            return false;
        //        }

        //        CascadeClassifier faceCascade = new CascadeClassifier(cascadePath);

        //        // Detect faces
        //        Rectangle[] capturedFaces = faceCascade.DetectMultiScale(matCaptured, 1.1, 5);
        //        Rectangle[] storedFaces = faceCascade.DetectMultiScale(matStored, 1.1, 5);

        //        if (capturedFaces.Length == 0 || storedFaces.Length == 0)
        //        {
        //            Console.WriteLine("No face detected in one or both images.");
        //            return false;
        //        }

        //        // Extract and normalize face regions
        //        Mat capturedFace = new Mat(matCaptured, capturedFaces[0]);
        //        Mat storedFace = new Mat(matStored, storedFaces[0]);

        //        CvInvoke.Resize(capturedFace, capturedFace, new Size(100, 100));
        //        CvInvoke.Resize(storedFace, storedFace, new Size(100, 100));

        //        CvInvoke.EqualizeHist(capturedFace, capturedFace);
        //        CvInvoke.EqualizeHist(storedFace, storedFace);

        //        // Initialize LBPH recognizer with adjusted parameters
        //        using (var faceRecognizer = new LBPHFaceRecognizer(2, 16, 8, 8, 100)) // 100 is a high threshold to force manual check
        //        {
        //            VectorOfMat trainingImages = new VectorOfMat();
        //            trainingImages.Push(storedFace);

        //            VectorOfInt labels = new VectorOfInt(new int[] { 1 });
        //            faceRecognizer.Train(trainingImages, labels);

        //            var result = faceRecognizer.Predict(capturedFace);

        //            Console.WriteLine($"Prediction Label: {result.Label}, Distance: {result.Distance}");

        //            // Use stricter threshold
        //            return result.Label == 1 && result.Distance <= 55; // Tighter threshold for better accuracy
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine("Error in face verification: " + ex.Message);
        //        return false;
        //    }
        //}

        private bool VerifyFace(Bitmap captured, Bitmap stored)
        {
            try
            {
                Mat matCaptured = BitmapToMat(captured);
                Mat matStored = BitmapToMat(stored);


                CvInvoke.CvtColor(matCaptured, matCaptured, Emgu.CV.CvEnum.ColorConversion.Bgr2Gray);
                CvInvoke.CvtColor(matStored, matStored, Emgu.CV.CvEnum.ColorConversion.Bgr2Gray);


                string cascadePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/Cascades/haarcascade_frontalface_default.xml");
                if (!System.IO.File.Exists(cascadePath))
                {
                    Console.WriteLine("Error: Haarcascade file not found!");
                    return false;
                }

                CascadeClassifier faceCascade = new CascadeClassifier(cascadePath);
                Rectangle[] capturedFaces = faceCascade.DetectMultiScale(matCaptured, 1.1, 5);
                Rectangle[] storedFaces = faceCascade.DetectMultiScale(matStored, 1.1, 5);


                if (capturedFaces.Length == 0 || storedFaces.Length == 0)
                {
                    Console.WriteLine("No face detected in one or both images.");
                    return false;
                }




                Mat capturedFace = new Mat(matCaptured, capturedFaces[0]);
                Mat storedFace = new Mat(matStored, storedFaces[0]);


                CvInvoke.Resize(capturedFace, capturedFace, new Size(100, 100));
                CvInvoke.Resize(storedFace, storedFace, new Size(100, 100));


                using (var faceRecognizer = new LBPHFaceRecognizer(1, 8, 8, 8, 96))
                {
                    CvInvoke.EqualizeHist(capturedFace, capturedFace);
                    CvInvoke.EqualizeHist(storedFace, storedFace);

                    VectorOfMat trainingImages = new VectorOfMat();
                    trainingImages.Push(storedFace);
                    VectorOfInt labels = new VectorOfInt(new int[] { 1 });

                    faceRecognizer.Train(trainingImages, labels);
                    var result = faceRecognizer.Predict(capturedFace);

                    Console.WriteLine($"Prediction Label: {result.Label}, Distance: {result.Distance}");

                    return result.Label == 1 && result.Distance <= 96;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error in face verification: " + ex.Message);
                return false;
            }
        }





        private Mat BitmapToMat(Bitmap bitmap)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
                byte[] imageData = ms.ToArray();

                Mat mat = new Mat();
                CvInvoke.Imdecode(new VectorOfByte(imageData), ImreadModes.Color, mat);

                if (mat.IsEmpty)
                {
                    Console.WriteLine("Error: Image conversion failed!");
                }

                return mat;
            }
        }
        private void SaveBase64ImageToFile(string base64String, string filePath)
        {
            try
            {
                byte[] imageBytes = Convert.FromBase64String(base64String.Split(',')[1]);
                using (MemoryStream ms = new MemoryStream(imageBytes))
                {
                    using (Bitmap bmp = new Bitmap(ms))
                    {
                        bmp.Save(filePath, System.Drawing.Imaging.ImageFormat.Jpeg);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error saving Base64 image to file: " + ex.Message);
            }
        }
        //private Mat DetectFace(Mat image)
        //{
        //    string modelPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/Cascades/res10_300x300_ssd_iter_140000_fp16.caffemodel");
        //    string protoPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/Cascades/deploy.prototxt");

        //    if (!System.IO.File.Exists(modelPath) || !System.IO.File.Exists(protoPath))
        //    {
        //        Console.WriteLine("Error: Face detection model files not found!");
        //        return null;
        //    }

        //    Net faceNet = DnnInvoke.ReadNetFromCaffe(protoPath, modelPath);
        //    Mat blob = DnnInvoke.BlobFromImage(image, 1.0, new Size(300, 300), new MCvScalar(104, 177, 123));
        //    faceNet.SetInput(blob);
        //    Mat detections = faceNet.Forward();

        //    Array detectionArray = detections.GetData();
        //    float[] detectionData = detectionArray.Cast<float>().ToArray();
        //    int numDetections = detections.SizeOfDimension[2];

        //    for (int i = 0; i < numDetections; i++)
        //    {
        //        float confidence = detectionData[i * 7 + 2];

        //        if (confidence > 0.95) // More strict confidence level
        //        {
        //            int x1 = (int)(detectionData[i * 7 + 3] * image.Width);
        //            int y1 = (int)(detectionData[i * 7 + 4] * image.Height);
        //            int x2 = (int)(detectionData[i * 7 + 5] * image.Width);
        //            int y2 = (int)(detectionData[i * 7 + 6] * image.Height);

        //            Rectangle faceRect = new Rectangle(x1, y1, x2 - x1, y2 - y1);
        //            return new Mat(image, faceRect);
        //        }
        //    }

        //    return null;
        //}



        //private bool VerifyFace(Bitmap captured, Bitmap stored)
        //{
        //    try
        //    {
        //        Mat matCaptured = BitmapToMat(captured);
        //        Mat matStored = BitmapToMat(stored);

        //        Mat capturedFace = DetectFace(matCaptured);
        //        Mat storedFace = DetectFace(matStored);

        //        if (capturedFace == null || storedFace == null)
        //        {
        //            Console.WriteLine("No face detected in one or both images.");
        //            return false;
        //        }


        //        CvInvoke.Resize(capturedFace, capturedFace, new Size(96, 96));
        //        CvInvoke.Resize(storedFace, storedFace, new Size(96, 96));

        //        float[] capturedEmbedding = GetFaceEmbedding(capturedFace);
        //        float[] storedEmbedding = GetFaceEmbedding(storedFace);

        //        double distance = CalculateEuclideanDistance(capturedEmbedding, storedEmbedding);
        //        Console.WriteLine($"[FaceMatch] Euclidean Distance: {distance}");

        //        return distance < 0.40;
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine("Error in face verification: " + ex.Message);
        //        return false;
        //    }
        //}



        //private float[] GetFaceEmbedding(Mat face)
        //{
        //    string modelPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/Cascades/nn4.small2.v1.t7");
        //    if (!System.IO.File.Exists(modelPath))
        //    {
        //        Console.WriteLine("Error: Face recognition model file not found!");
        //        return null;
        //    }

        //    Net faceRecognizer = DnnInvoke.ReadNetFromTorch(modelPath);
        //    Mat blob = DnnInvoke.BlobFromImage(face, 1.0 / 255, new Size(96, 96), new MCvScalar(0, 0, 0), true, false);
        //    faceRecognizer.SetInput(blob);
        //    Mat output = faceRecognizer.Forward();

        //    return output.GetData().Cast<float>().ToArray();
        //}


        //private double CalculateEuclideanDistance(float[] vec1, float[] vec2)
        //{
        //    if (vec1.Length != vec2.Length) return double.MaxValue;

        //    double sum = 0;
        //    for (int i = 0; i < vec1.Length; i++)
        //    {
        //        sum += Math.Pow(vec1[i] - vec2[i], 2);
        //    }

        //    return Math.Sqrt(sum);
        //}


        //[HttpPost]
        //public IActionResult AttendanceData([FromBody] AttendanceRequest model)
        //{
        //    try
        //    {
        //        var UserId = HttpContext.Request.Cookies["Session"];
        //        var UserName = HttpContext.Request.Cookies["UserName"];
        //        if (string.IsNullOrEmpty(UserId))
        //            return Json(new { success = false, message = "User session not found!" });

        //        string Pno = UserId;
        //        string Name = UserName;

        //        string storedImagePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/Images/", $"{Pno}-{Name}.jpg");
        //        if (!System.IO.File.Exists(storedImagePath))
        //        {
        //            return Json(new { success = false, message = "Stored image not found!" });
        //        }


        //        string capturedImagePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/Images/", $"{Pno}-Captured-{DateTime.Now.Ticks}.jpg");


        //        SaveBase64ImageToFile(model.ImageData, capturedImagePath);

        //        bool isFaceMatched = false;


        //        using (Bitmap capturedImage = new Bitmap(capturedImagePath))
        //        using (Bitmap storedImage = new Bitmap(storedImagePath))
        //        {
        //            isFaceMatched = VerifyFace(capturedImage, storedImage);
        //        }


        //        System.IO.File.Delete(capturedImagePath);

        //        if (isFaceMatched)
        //        {
        //            string currentDate = DateTime.Now.ToString("yyyy/MM/dd");
        //            string currentTime = DateTime.Now.ToString("HH:mm");

        //            if (model.Type == "Punch In")
        //            {
        //                string capturedImage = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/Images/", $"{Pno}-Captured.jpg");


        //                SaveBase64ImageToFile(model.ImageData, capturedImage);

        //                StoreData(currentDate, currentTime, null, Pno, model.ImageData);
        //            }
        //            else
        //            {
        //                StoreData(currentDate, null, currentTime, Pno, model.ImageData);
        //            }

        //            return Json(new { success = true, message = "Attendance recorded successfully." });
        //        }
        //        else
        //        {
        //            return Json(new { success = false, message = "Face does not match!" });
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        return Json(new { success = false, message = ex.Message });
        //    }
        //}


        //private bool VerifyFace(Bitmap captured, Bitmap stored)
        //{
        //    try
        //    {
        //        Mat matCaptured = BitmapToMat(captured);
        //        Mat matStored = BitmapToMat(stored);


        //        CvInvoke.CvtColor(matCaptured, matCaptured, Emgu.CV.CvEnum.ColorConversion.Bgr2Gray);
        //        CvInvoke.CvtColor(matStored, matStored, Emgu.CV.CvEnum.ColorConversion.Bgr2Gray);


        //        string cascadePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/Cascades/haarcascade_frontalface_default.xml");
        //        if (!System.IO.File.Exists(cascadePath))
        //        {
        //            Console.WriteLine("Error: Haarcascade file not found!");
        //            return false;
        //        }

        //        CascadeClassifier faceCascade = new CascadeClassifier(cascadePath);
        //        Rectangle[] capturedFaces = faceCascade.DetectMultiScale(matCaptured, 1.1, 5);
        //        Rectangle[] storedFaces = faceCascade.DetectMultiScale(matStored, 1.1, 5);


        //        if (capturedFaces.Length == 0 || storedFaces.Length == 0)
        //        {
        //            Console.WriteLine("No face detected in one or both images.");
        //            return false;
        //        }




        //        Mat capturedFace = new Mat(matCaptured, capturedFaces[0]);
        //        Mat storedFace = new Mat(matStored, storedFaces[0]);


        //        CvInvoke.Resize(capturedFace, capturedFace, new Size(100, 100));
        //        CvInvoke.Resize(storedFace, storedFace, new Size(100, 100));


        //        using (var faceRecognizer = new LBPHFaceRecognizer(1, 8, 8, 8, 97))
        //        {
        //            CvInvoke.EqualizeHist(capturedFace, capturedFace);
        //            CvInvoke.EqualizeHist(storedFace, storedFace);

        //            VectorOfMat trainingImages = new VectorOfMat();
        //            trainingImages.Push(storedFace);
        //            VectorOfInt labels = new VectorOfInt(new int[] { 1 });

        //            faceRecognizer.Train(trainingImages, labels);
        //            var result = faceRecognizer.Predict(capturedFace);

        //            Console.WriteLine($"Prediction Label: {result.Label}, Distance: {result.Distance}");

        //            return result.Label == 1 && result.Distance <= 97;
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine("Error in face verification: " + ex.Message);
        //        return false;
        //    }
        //}

        public void StoreData(string ddMMyy, string tmIn, string tmOut, string Pno)
        {
            using (var connection = new SqlConnection(configuration.GetConnectionString("RFID")))
            {
                connection.Open();

                if (!string.IsNullOrEmpty(tmIn))
                {
                    var query = @"
                INSERT INTO T_TRBDGDAT_EARS(TRBDGDA_BD_DATE, TRBDGDA_BD_TIME, TRBDGDA_BD_INOUT, TRBDGDA_BD_READER,
                TRBDGDA_BD_CHKHS, TRBDGDA_BD_SUBAREA, TRBDGDA_BD_PNO) 
                VALUES 
                (@TRBDGDA_BD_DATE,
                @TRBDGDA_BD_TIME, 
                @TRBDGDA_BD_INOUT,
                @TRBDGDA_BD_READER, 
                @TRBDGDA_BD_CHKHS, 
                @TRBDGDA_BD_SUBAREA, 
                @TRBDGDA_BD_PNO)";

                    var parameters = new
                    {
                        TRBDGDA_BD_DATE = ddMMyy,
                        TRBDGDA_BD_TIME = ConvertTimeToMinutes(tmIn),
                        TRBDGDA_BD_INOUT = "I",
                        TRBDGDA_BD_READER = "2",
                        TRBDGDA_BD_CHKHS = "2",
                        TRBDGDA_BD_SUBAREA = "JUSC12",
                        TRBDGDA_BD_PNO = Pno
                    };

                    connection.Execute(query, parameters);

                    var Punchquery = @"
                INSERT INTO T_TRPUNCHDATA_EARS(PDE_PUNCHDATE,PDE_PUNCHTIME,PDE_INOUT,PDE_MACHINEID,
                PDE_READERNO,PDE_CHKHS,PDE_SUBAREA,PDE_PSRNO) 
                VALUES 
                (@PDE_PUNCHDATE,
                @PDE_PUNCHTIME, 
                @PDE_INOUT,
                @PDE_MACHINEID, 
                @PDE_READERNO, 
                @PDE_CHKHS, 
                @PDE_SUBAREA, 
                @PDE_PSRNO)";

                    var parameters2 = new
                    {
                        PDE_PUNCHDATE = ddMMyy,
                        PDE_PUNCHTIME = tmIn,
                        PDE_INOUT = "I",
                        PDE_MACHINEID = "2",
                        PDE_READERNO = "2",
                        PDE_CHKHS = "2",
                        PDE_SUBAREA = "JUSC12",
                        PDE_PSRNO = Pno
                    };

                    connection.Execute(Punchquery, parameters2);
                }

                if (!string.IsNullOrEmpty(tmOut))
                {
                    var queryOut = @"
                INSERT INTO T_TRBDGDAT_EARS(TRBDGDA_BD_DATE, TRBDGDA_BD_TIME, TRBDGDA_BD_INOUT, TRBDGDA_BD_READER, 
                 TRBDGDA_BD_CHKHS, TRBDGDA_BD_SUBAREA, TRBDGDA_BD_PNO) 
                VALUES 
                (@TRBDGDA_BD_DATE,
                @TRBDGDA_BD_TIME, 
                @TRBDGDA_BD_INOUT, 
                @TRBDGDA_BD_READER, 
                @TRBDGDA_BD_CHKHS,
                @TRBDGDA_BD_SUBAREA,
                @TRBDGDA_BD_PNO)";

                    var parametersOut = new
                    {
                        TRBDGDA_BD_DATE = ddMMyy,
                        TRBDGDA_BD_TIME = ConvertTimeToMinutes(tmOut),
                        TRBDGDA_BD_INOUT = "O",
                        TRBDGDA_BD_READER = "2",
                        TRBDGDA_BD_CHKHS = "2",
                        TRBDGDA_BD_SUBAREA = "JUSC12",
                        TRBDGDA_BD_PNO = Pno
                    };

                    connection.Execute(queryOut, parametersOut);

                    var Punchquery = @"
                INSERT INTO T_TRPUNCHDATA_EARS(PDE_PUNCHDATE,PDE_PUNCHTIME,PDE_INOUT,PDE_MACHINEID,
                PDE_READERNO,PDE_CHKHS,PDE_SUBAREA,PDE_PSRNO) 
                VALUES 
                (@PDE_PUNCHDATE,
                @PDE_PUNCHTIME, 
                @PDE_INOUT,
                @PDE_MACHINEID, 
                @PDE_READERNO, 
                @PDE_CHKHS, 
                @PDE_SUBAREA, 
                @PDE_PSRNO)";

                    var parameters2 = new
                    {
                        PDE_PUNCHDATE = ddMMyy,
                        PDE_PUNCHTIME = tmOut,
                        PDE_INOUT = "O",
                        PDE_MACHINEID = "2",
                        PDE_READERNO = "2",
                        PDE_CHKHS = "2",
                        PDE_SUBAREA = "JUSC12",
                        PDE_PSRNO = Pno
                    };

                    connection.Execute(Punchquery, parameters2);
                }

            //    if (!string.IsNullOrEmpty(capturedImage))
            //    {
            //        Guid ID = Guid.NewGuid(); 

                    
            //        byte[] imageBytes = Convert.FromBase64String(capturedImage.Split(',')[1]);

            //        string fileName = $"{ID}_{Pno}.txt";

            //        string folderPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/CapturedImage");

            //        string filePath = Path.Combine(folderPath, fileName);

            //        if (!Directory.Exists(folderPath))
            //        {
            //            Directory.CreateDirectory(folderPath);
            //        }

            //        System.IO.File.WriteAllBytes(filePath, imageBytes); 

                   
            //        var query = @"
            //INSERT INTO App_ImageDetail(ID, Pno, FileName) 
            //VALUES (@ID, @Pno, @FileName)";

            //        var parameters = new
            //        {
            //            ID = ID,
            //            Pno = Pno,
            //            FileName = fileName
            //        };

            //        connection.Execute(query, parameters);
            //    }

            }
        }


        private int ConvertTimeToMinutes(string time)
        {
            var strtm = time.Split(':');
            return (Convert.ToInt32(strtm[0]) * 60) + Convert.ToInt32(strtm[1]);
        }

        public IActionResult AttendanceReport(string url)
        {
            
            string psrNo = HttpContext.Request.Cookies["Session"]; 

            if (string.IsNullOrEmpty(psrNo))
            {
                return RedirectToAction("Login", "User"); 
            }

           
            if (string.IsNullOrEmpty(url))
            {
                
                //url = $"https://services.juscoltd.com/TSUISLARSRPT/Webform1.aspx?pno={psrNo}";
                url = $"https://servicesdev.juscoltd.com/AttendanceReport/Webform1.aspx?pno={psrNo}";
               
            }
            else
            {
                url += $"?pno={psrNo}";
            }

            ViewBag.ReportUrl = url; 
            return View();
        }

        public IActionResult UploadImage()
        {
            var pno = HttpContext.Request.Cookies["Session"];
            var userName = HttpContext.Request.Cookies["UserName"];

            if (string.IsNullOrEmpty(pno) || string.IsNullOrEmpty(userName))
            {
                return RedirectToAction("Login","User");
            }

            var PnoEnameList = context1.AppEmployeeMasters
                    .Select(x => new
                    {
                        Pno = x.Pno,
                        Ename = x.Ename,
                    })
                    .ToList();
            ViewBag.PnoEnameList = PnoEnameList;


            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadImage(string Pno, string Name, string photoData)
        {
            if (!string.IsNullOrEmpty(photoData) && !string.IsNullOrEmpty(Pno) && !string.IsNullOrEmpty(Name))
            {
                try
                {
                    byte[] imageBytes = Convert.FromBase64String(photoData.Split(',')[1]);

                   
                    string folderPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/Images");

                    
                    if (!Directory.Exists(folderPath))
                    {
                        Directory.CreateDirectory(folderPath);
                    }

                   
                    string fileName = $"{Pno}-{Name}.jpg";
                    string filePath = Path.Combine(folderPath, fileName);

                  
                    System.IO.File.WriteAllBytes(filePath, imageBytes);

                   
                    var person = new AppPerson
                    {
                        Pno = Pno,
                        Name = Name,
                        Image = fileName
                    };

                    context.AppPeople.Add(person);
                    await context.SaveChangesAsync();

                   
                    return Ok(new { success = true, message = "Image uploaded and data saved successfully." });
                }
                catch (Exception ex)
                {
                  
                    return StatusCode(500, new { success = false, message = "Error saving image: " + ex.Message });
                }
            }

            return BadRequest(new { success = false, message = "Missing required fields!" });
        }

        //public IActionResult FaceRecognisation()
        //{
        //     string storedImagePath = "wwwroot/Images/151514-Shashi Kumar.jpg";
        //    string capturedImagePath = "wwwroot/Images/151514-Shashi Kumar.jpg";

        //    bool isFaceMatched = CompareFaces(storedImagePath, capturedImagePath);

        //    if (isFaceMatched)
        //    {
        //        Console.WriteLine("Face Matched!");
        //    }
        //    else
        //    {
        //        Console.WriteLine("Face Does Not Match!");
        //    }

        //    return View(); 
        //}


        static bool CompareFaces(string storedImagePath, string capturedImagePath)
        {
            try
            {
                Mat storedImage = CvInvoke.Imread(storedImagePath, ImreadModes.Grayscale);
                Mat capturedImage = CvInvoke.Imread(capturedImagePath, ImreadModes.Grayscale);

                if (storedImage.IsEmpty || capturedImage.IsEmpty)
                {
                    Console.WriteLine("Error: One or both images are empty!");
                    return false;
                }

                string cascadePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "D:/Irshad_Project/GFAS/GFAS/wwwroot/Cascades/haarcascade_frontalface_default.xml");
                Console.WriteLine($"Cascade Path: {cascadePath}");

                if (!System.IO.File.Exists(cascadePath))
                {
                    Console.WriteLine("Error: Haarcascade file not found!");
                    return false;
                }

                CascadeClassifier faceCascade = new CascadeClassifier(cascadePath);

                Rectangle[] storedFaces = faceCascade.DetectMultiScale(storedImage, 1.1, 5);
                Rectangle[] capturedFaces = faceCascade.DetectMultiScale(capturedImage, 1.1, 5);

                if (storedFaces.Length == 0 || capturedFaces.Length == 0)
                {
                    Console.WriteLine("No face detected in one or both images.");
                    return false;
                }

                Mat storedFace = new Mat(storedImage, storedFaces[0]);
                Mat capturedFace = new Mat(capturedImage, capturedFaces[0]);

                CvInvoke.Resize(storedFace, storedFace, new Size(100, 100));
                CvInvoke.Resize(capturedFace, capturedFace, new Size(100, 100));

                LBPHFaceRecognizer recognizer = new LBPHFaceRecognizer(1, 8, 8, 8, 100);
                VectorOfMat trainingImages = new VectorOfMat();
                VectorOfInt labels = new VectorOfInt(new int[] { 1 });

                trainingImages.Push(storedFace);
                recognizer.Train(trainingImages, labels);

                var result = recognizer.Predict(capturedFace);

                Console.WriteLine($"Prediction Label: {result.Label}, Distance: {result.Distance}");

                return result.Label == 1 && result.Distance < 50;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error in face comparison: " + ex.Message);
                return false;
            }
        }


        public IActionResult ImageViewer()
        {
            var pno = HttpContext.Request.Cookies["Session"];
            var userName = HttpContext.Request.Cookies["UserName"];

            if (string.IsNullOrEmpty(pno) || string.IsNullOrEmpty(userName))
            {
                return RedirectToAction("Login","User");
            }

            var folderPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Images");

           
            var baseImageFile = $"{pno}-{userName}.jpg";
            var baseImagePath = Path.Combine(folderPath, baseImageFile);
            ViewBag.BaseImagePath = System.IO.File.Exists(baseImagePath) ? $"/TSUISLARS/Images/{baseImageFile}" : null;

            
            var capturedImageFile = $"{pno}-Captured.jpg";
            var capturedImagePath = Path.Combine(folderPath, capturedImageFile);
            ViewBag.CapturedImagePath = System.IO.File.Exists(capturedImagePath) ? $"/TSUISLARS/Images/{capturedImageFile}" : null;

            
            string attendanceLocation = "N/A";
            string connectionString = GetRFIDConnectionString();

            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string query = @"
            SELECT ps.Worksite 
            FROM TSUISLRFIDDB.DBO.App_Position_Worksite AS ps
            INNER JOIN TSUISLRFIDDB.DBO.App_Emp_position AS es ON es.position = ps.position
            WHERE es.Pno = @UserId";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@UserId", pno);
                    var result = cmd.ExecuteScalar();
                    if (result != null)
                    {
                        attendanceLocation = result.ToString();
                    }
                }
            }


            ViewBag.AttendanceLocation = attendanceLocation;

            return View();
        }


    }

}
