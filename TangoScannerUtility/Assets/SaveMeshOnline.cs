using UnityEngine;
using System.Collections;
using System.IO;
using System.Text;
using System.Linq;


public class SaveMeshOnline : MonoBehaviour {
	
	private static int StartIndex = 0;
	
	public static void Start()
	{
		StartIndex = 0;
	}
	public static void End()
	{
		StartIndex = 0;
	}
	
	
	public static string MeshToString(MeshFilter mf, Transform t) 
	{    
		Vector3 s = t.localScale;
		Vector3 p = t.localPosition;
		Quaternion r = t.localRotation;
		
		
		int numVertices = 0;
		Mesh m = mf.sharedMesh;
		if (!m) {
			return "####Error####";
		}
		Material[] mats = mf.GetComponent<Renderer> ().sharedMaterials;
		
		StringBuilder sb = new StringBuilder ();
		
		Vector3[] normals = m.normals; 
		for (int i=0; i<normals.Length; i++) // remove this if your exported mesh have faces on wrong side
			normals [i] = -normals [i];
		m.normals = normals;
		
		m.triangles = m.triangles.Reverse ().ToArray (); //
		
		foreach (Vector3 vv in m.vertices) {
			Vector3 v = t.TransformPoint (vv);
			numVertices++;
			sb.Append (string.Format ("v {0} {1} {2}\n", v.x, v.y, -v.z));
		}
		sb.Append ("\n");
		foreach (Vector3 nn in m.normals) {
			Vector3 v = r * nn;
			sb.Append (string.Format ("vn {0} {1} {2}\n", -v.x, -v.y, v.z));
		}
		sb.Append ("\n");
		foreach (Vector3 v in m.uv) {
			sb.Append (string.Format ("vt {0} {1}\n", v.x, v.y));
		}
		for (int material=0; material < m.subMeshCount; material ++) {
			sb.Append ("\n");
			sb.Append ("usemtl ").Append (mats [material].name).Append ("\n");
			sb.Append ("usemap ").Append (mats [material].name).Append ("\n");
			
			int[] triangles = m.GetTriangles (material);
			for (int i=0; i<triangles.Length; i+=3) {
				sb.Append (string.Format ("f {0}/{0}/{0} {1}/{1}/{1} {2}/{2}/{2}\n", 
				                          triangles [i] + 1 + StartIndex, triangles [i + 1] + 1 + StartIndex, triangles [i + 2] + 1 + StartIndex));
			}
		}
		
		for (int i=0; i<normals.Length; i++) // remove this if your exported mesh have faces on wrong side
			normals [i] = -normals [i];
		m.normals = normals;
		
		m.triangles = m.triangles.Reverse ().ToArray (); //
		
		StartIndex += numVertices;
		return sb.ToString ();
	}

    /*public class Tuple<T1, T2, T3, T4>
    {
        public T1 First { get; private set; }
        public T2 Second { get; private set; }
        public T3 Third { get; private set; }
        public T4 Fourth { get; private set; }
        internal Tuple(T1 first, T2 second, T3 third, T4 fourth)
        {
            First = first;
            Second = second;
            Third = third;
            Fourth = fourth;
        }
    }

    public static class Tuple
    {
        public static Tuple<T1, T2, T3, T4> New<T1, T2, T3, T4>(T1 first, T2 second, T3 third, T4 fourth)
        {
            var tuple = new Tuple<T1, T2, T3, T4>(first, second, third, fourth);
            return tuple;
        }
    }*/
    public class Tuple<T1, T2>
    {
        public T1 First { get; private set; }
        public T2 Second { get; private set; }

        internal Tuple(T1 first, T2 second)
        {
            First = first;
            Second = second;

        }
    }

    public static class Tuple
    {
        public static Tuple<T1, T2> New<T1, T2>(T1 first, T2 second)
        {
            var tuple = new Tuple<T1, T2>(first, second);
            return tuple;
        }
    }

    public static Tuple<int, int> MeshToStringPLY(MeshFilter mf, Transform t, StreamWriter vertSw, StreamWriter faceSw) 
	{    
		Vector3 s = t.localScale;
		Vector3 p = t.localPosition;
		Quaternion r = t.localRotation;

		int numVertices = 0;
		Mesh m = mf.sharedMesh;
		if (!m) {
			return new Tuple<int, int>(0,0);
		}
		//Material[] mats = mf.GetComponent<Renderer> ().sharedMaterials;

		/*Vector3[] normals = m.normals; 
		for (int i=0; i<normals.Length; i++) // remove this if your exported mesh have faces on wrong side
			normals [i] = -normals [i];
		m.normals = normals;
        */

		//m.triangles = m.triangles.Reverse ().ToArray (); //

		for (int i=0; i<m.vertexCount; i++) {
			Vector3 vv  = m.vertices[i];
			Vector3 v = t.TransformPoint (vv);
            //Mesh has a lot of empty points, don't save them ... and they
            // always are at the end of the list?
            if (Mathf.Abs(v.x) < 0.00001 &&
                Mathf.Abs(v.y) < 0.00001 &&
                Mathf.Abs(v.z) < 0.00001 &&
                m.colors[i].r < 0.0001 &&
                m.colors[i].g < 0.0001 &&
                m.colors[i].b < 0.0001)
                break;

			numVertices++;
			vertSw.Write (string.Format ("{0} {1} {2} {3} {4} {5}\n", v.x, v.y, -v.z, 
				(int)(255.9*m.colors[i].r), (int)(255.9*m.colors[i].g), (int)(255.9*m.colors[i].b)));
		}

        int numTris = 0;
		for (int material=0; material < m.subMeshCount; material ++) {
			int[] triangles = m.GetTriangles (material);
			for (int i=0; i<triangles.Length; i+=3) {
                //Don't add degerenate triangles
                if (triangles[i + 2] != triangles[i + 1] &&
                    triangles[i + 1] != triangles[i] &&
                    triangles[i + 2] != triangles[i])
                {
                    numTris++;
                    faceSw.Write(string.Format("3 {0} {1} {2}\n",
                        triangles[i + 2] + StartIndex, triangles[i + 1] + StartIndex, triangles[i + 0] + StartIndex));
                }
			}
		}

        vertSw.Flush();
        faceSw.Flush();

		/*for (int i=0; i<normals.Length; i++) // remove this if your exported mesh have faces on wrong side
			normals [i] = -normals [i];
		m.normals = normals;*/

		//m.triangles = m.triangles.Reverse ().ToArray (); //

		StartIndex += numVertices;
		return new Tuple<int, int>(numVertices, numTris);
	}
	
	public void DoExport(bool makeSubmeshes)
	{
		AndroidHelper.ShowAndroidToastMessage ("in");
		string meshName = gameObject.name;
		string fileName = Application.persistentDataPath+"/"+gameObject.name+".obj"; // you can also use: "/storage/sdcard1/" +gameObject.name+".obj"
		
		Start();
		
		StringBuilder meshString = new StringBuilder();
		
		meshString.Append("#" + meshName + ".obj"
		                  + "\n#" + System.DateTime.Now.ToLongDateString() 
		                  + "\n#" + System.DateTime.Now.ToLongTimeString()
		                  + "\n#-------" 
		                  + "\n\n");
		
		Transform t = transform;
		
		Vector3 originalPosition = t.position;
		t.position = Vector3.zero;
		
		if (!makeSubmeshes)
		{
			meshString.Append("g ").Append(t.name).Append("\n");
		}
		meshString.Append(processTransform(t, makeSubmeshes));
		
		WriteToFile(meshString.ToString(),fileName);
		
		t.position = originalPosition;
		
		End();
		Debug.Log("Exported Mesh: " + fileName);
	}

	public void DoExportPLY(bool makeSubmeshes)
	{
		AndroidHelper.ShowAndroidToastMessage ("in");
		string meshName = gameObject.name;
		string mainFileName = Application.persistentDataPath+"/"+gameObject.name+".ply"; // you can also use: "/storage/sdcard1/" +gameObject.name+".obj"
        string headerFileName = Application.persistentDataPath + "/" + gameObject.name + ".ply.header";
        string vertexFileName = Application.persistentDataPath + "/" + gameObject.name + ".ply.verts";
        string faceFileName = Application.persistentDataPath + "/" + gameObject.name + ".ply.faces";

        StreamWriter headerSw = new StreamWriter(headerFileName);    
        StreamWriter vertSw = new StreamWriter(vertexFileName);
        StreamWriter faceSw = new StreamWriter(faceFileName);

        AndroidHelper.ShowAndroidToastMessage(mainFileName);

		Start();

		Transform t = transform;

		Vector3 originalPosition = t.position;
		t.position = Vector3.zero;

		if (!makeSubmeshes)
		{
			//meshString.Append("g ").Append(t.name).Append("\n");
		}
		var ret = processTransformPLY(t, makeSubmeshes, vertSw, faceSw);

        vertSw.Flush();
        vertSw.Close();
        faceSw.Flush();
        faceSw.Close();

        headerSw.Write("ply" +
            "\nformat ascii 1.0" +
            "\nelement vertex " + ret.First +
            "\nproperty float x" +
            "\nproperty float y" +
            "\nproperty float z" +
            "\nproperty uchar red" +
            "\nproperty uchar green" +
            "\nproperty uchar blue" +
            "\nelement face " + ret.Second +
            "\nproperty list uchar int vertex_index" +
            "\nend_header\n");
        headerSw.Flush();
        headerSw.Close();

        const int chunkSize = 2 * 1024; // 2KB
        var inputFiles = new[] { headerFileName, vertexFileName, faceFileName };
        using (var output = File.Create(mainFileName))
        {
            foreach (var file in inputFiles)
            {
                using (var input = File.OpenRead(file))
                {
                    var buffer = new byte[chunkSize];
                    int bytesRead;
                    while ((bytesRead = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        output.Write(buffer, 0, bytesRead);
                    }
                }
            }
        }

        t.position = originalPosition;

		End();
		Debug.Log("Exported Mesh: " + mainFileName);
	}
	
	static string processTransform(Transform t, bool makeSubmeshes)
	{
		StringBuilder meshString = new StringBuilder();
		
		meshString.Append("#" + t.name
		                  + "\n#-------" 
		                  + "\n");
		
		if (makeSubmeshes)
		{
			meshString.Append("g ").Append(t.name).Append("\n");
		}
		
		MeshFilter mf = t.GetComponent<MeshFilter>();
		if (mf)
		{
			meshString.Append(MeshToString(mf, t));
		}
		
		for(int i = 0; i < t.childCount; i++)
		{
			meshString.Append(processTransform(t.GetChild(i), makeSubmeshes));
		}
		
		return meshString.ToString();
	}

	static Tuple<int, int> processTransformPLY(Transform t, bool makeSubmeshes, StreamWriter vertSw, StreamWriter faceSw)
	{
        int vertCount = 0;
        int triCount = 0;

        MeshFilter mf = t.GetComponent<MeshFilter>();
		if (mf)
		{
            var ret = MeshToStringPLY(mf, t, vertSw, faceSw);
            vertCount += ret.First;
            triCount += ret.Second;
		}

		for(int i = 0; i < t.childCount; i++)
		{
            var ret = processTransformPLY(t.GetChild(i), makeSubmeshes, vertSw, faceSw);
            vertCount += ret.First;
            triCount += ret.Second;
        }

        return new Tuple<int, int>(vertCount, triCount);
	}
	
	static void WriteToFile(string s, string filename)
	{
		using (StreamWriter sw = new StreamWriter(filename)) 
		{
			sw.Write(s);
		}
	}
}