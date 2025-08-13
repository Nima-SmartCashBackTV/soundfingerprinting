

# **AdDetector — Setup & Run Guide**

## **1. Get the Code**

```powershell
git clone https://github.com/addictedcs/soundfingerprinting.git
cd soundfingerprinting\AdDetector
```

---

## **2. Add References / Packages**

### **2A. Building Inside the Cloned Repo**

The sample already references:

* `src\SoundFingerprinting`
* `src\SoundFingerprinting.Emy`

Just restore dependencies:

```powershell
dotnet restore
```

---

### **2B. Building Your Own Project (Outside the Repo)**

Choose **one** approach:

#### **(i) Project References to Local Source**

```powershell
dotnet new console -n AdDetector
cd AdDetector
dotnet add reference ..\soundfingerprinting\src\SoundFingerprinting\SoundFingerprinting.csproj
dotnet add reference ..\soundfingerprinting\src\SoundFingerprinting.Emy\SoundFingerprinting.Emy.csproj
dotnet restore
```

#### **(ii) NuGet Packages (No Local Source)**

```powershell
dotnet add package SoundFingerprinting
dotnet add package SoundFingerprinting.Emy
dotnet add package FFmpeg.AutoGen
dotnet add package FFmpeg.AutoGen.runtime.win-x64
dotnet restore
```

> **Tip:** NuGet-only is simpler. Project references are better if you plan to modify the library.

---

## **3. Install FFmpeg (for EmY)**

EmY uses FFmpeg via **FFmpeg.AutoGen** and requires **shared DLLs**.

### **Option A — NuGet Runtime (Recommended)**

If you added `FFmpeg.AutoGen.runtime.win-x64`, the needed FFmpeg DLLs are copied automatically. No manual copying required.

### **Option B — Manual Copy**

1. Download **`ffmpeg-7.0.2-full_build-shared.7z`** from
   [https://www.gyan.dev/ffmpeg/builds/](https://www.gyan.dev/ffmpeg/builds/)
2. Extract it.
3. Copy the following from its `bin` folder into:

   ```
   <your project>\bin\Release\net9.0\FFmpeg\bin\x64
   ```

   **Required DLLs:**

   * `avcodec-61.dll`
   * `avformat-61.dll`
   * `avutil-59.dll`
   * `swresample-5.dll`
   * `swscale-8.dll`

> This fixes `System.NotSupportedException` caused by older FFmpeg missing `av_channel_layout_from_mask`.

---

## **4. Prepare Data Folders**

The sample expects:

```
C:\small_data\ads
C:\small_data\queries
```

* Put **reference ads** (`.mp3`, `.mp4`, `.wav`, etc.) in `ads`
* Put **queries** (streams/clips to scan) in `queries`

To use different folders, update the paths in:

```
AdDetector\Program.cs
```

---

## **5. Build & Run**

```powershell
dotnet clean
dotnet build -c Release
dotnet run  -c Release
```

You should see:

```
✓ FFmpeg DLLs loaded from ...\FFmpeg\bin\x64
```

