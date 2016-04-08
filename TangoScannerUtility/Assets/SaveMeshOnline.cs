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

	public static string MeshToStringPLY(MeshFilter mf, Transform t) 
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

		sb.Append ( "ply" +
			"\nformat ascii 1.0" +
			"\nelement vertex " + m.vertexCount +
			"\nproperty float x" +
			"\nproperty float y" +
			"\nproperty float z" +
			"\nproperty uchar red" +                 
			"\nproperty uchar green" +
			"\nproperty uchar blue" +
			"\nelement face " + m.triangles.Length/3 +
			"\nproperty list uchar int vertex_index" +  
			"\nend_header\n" );

		Vector3[] normals = m.normals; 
		for (int i=0; i<normals.Length; i++) // remove this if your exported mesh have faces on wrong side
			normals [i] = -normals [i];
		m.normals = normals;

		m.triangles = m.triangles.Reverse ().ToArray (); //

		for (int i=0; i<m.vertexCount; i++) {
			Vector3 vv  = m.vertices[i];
			Vector3 v = t.TransformPoint (vv);
			numVertices++;
			sb.Append (string.Format ("{0} {1} {2} {3} {4} {5}\n", v.x, v.y, -v.z, 
				(int)(255.9*m.colors[i].r), (int)(255.9*m.colors[i].g), (int)(255.9*m.colors[i].b)));
		}
		/*sb.Append ("\n");
		foreach (Vector3 nn in m.normals) {
			Vector3 v = r * nn;
			sb.Append (string.Format ("vn {0} {1} {2}\n", -v.x, -v.y, v.z));
		}
		sb.Append ("\n");
		foreach (Vector3 v in m.uv) {
			sb.Append (string.Format ("vt {0} {1}\n", v.x, v.y));
		}*/
		for (int material=0; material < m.subMeshCount; material ++) {
			//sb.Append ("\n");
			//sb.Append ("usemtl ").Append (mats [material].name).Append ("\n");
			//sb.Append ("usemap ").Append (mats [material].name).Append ("\n");

			int[] triangles = m.GetTriangles (material);
			for (int i=0; i<triangles.Length; i+=3) {
				sb.Append (string.Format ("3 {0} {1} {2}\n", 
					triangles [i], triangles [i + 1], triangles [i + 2]));
			}
		}

		for (int i=0; i<normals.Length; i++) // remove this if your exported mesh have faces on wrong side
			normals [i] = -normals [i];
		m.normals = normals;

		m.triangles = m.triangles.Reverse ().ToArray (); //

		StartIndex += numVertices;
		return sb.ToString ();
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
		string fileName = Application.persistentDataPath+"/"+gameObject.name+".ply"; // you can also use: "/storage/sdcard1/" +gameObject.name+".obj"
        AndroidHelper.ShowAndroidToastMessage(fileName);

		Start();

		StringBuilder meshString = new StringBuilder();

		Transform t = transform;

		Vector3 originalPosition = t.position;
		t.position = Vector3.zero;

		if (!makeSubmeshes)
		{
			//meshString.Append("g ").Append(t.name).Append("\n");
		}
		meshString.Append(processTransformPLY(t, makeSubmeshes));

		WriteToFile(meshString.ToString(),fileName);

		t.position = originalPosition;

		End();
		Debug.Log("Exported Mesh: " + fileName);
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

	static string processTransformPLY(Transform t, bool makeSubmeshes)
	{
		StringBuilder meshString = new StringBuilder();

		MeshFilter mf = t.GetComponent<MeshFilter>();
		if (mf)
		{
			meshString.Append(MeshToStringPLY(mf, t));
		}

		//TODO: This is not going to work if there are child meshes
		for(int i = 0; i < t.childCount; i++)
		{
			meshString.Append(processTransformPLY(t.GetChild(i), makeSubmeshes));
		}

		return meshString.ToString();
	}
	
	static void WriteToFile(string s, string filename)
	{
		using (StreamWriter sw = new StreamWriter(filename)) 
		{
			sw.Write(s);
		}
	}
}