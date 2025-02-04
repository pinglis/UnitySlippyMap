// 
//  WMSTileLayer.cs
//  
//  Author:
//       Jonathan Derrough <jonathan.derrough@gmail.com>
//  
// Copyright (c) 2017 Jonathan Derrough
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.Collections;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

using ProjNet.CoordinateSystems;

using UnityEngine;
using UnityEngine.Networking;
using UnitySlippyMap.Helpers;

namespace UnitySlippyMap.Layers
{
	/// <summary>
	/// A class representing a Web Mapping Service tile layer.
	/// </summary>
	public class WMSTileLayerBehaviour : WebTileLayerBehaviour
	{
		#region Private members & properties

		/// <summary>
		/// Gets or sets the base URL.
		/// </summary>
		/// <value>The base URL.</value>
		public new string BaseURL
		{
			get { return baseURL; }
			set
			{
				if (value == null)
					throw new ArgumentNullException("value");
				if (value == String.Empty)
					throw new Exception("value cannot be empty");
				baseURLChanged = true;
				baseURL = value;
			}
		}

		/// <summary>
		/// The coma separated list of layers to be requested.
		/// </summary>
		private string layers = String.Empty;

		/// <summary>
		/// Gets or sets the layers.
		/// </summary>
		/// <value>The layers.</value>
		public string Layers
		{
			get { return layers; }
			set
			{
				layers = value;
				if (layers == null)
					layers = String.Empty;
				else
				{
					CheckLayers();
				}
			}
		}

		/// <summary>
		/// The Spatial Reference System of the layer.
		/// </summary>
		private ICoordinateSystem srs = GeographicCoordinateSystem.WGS84;

		/// <summary>
		/// Gets or sets the SRS.
		/// </summary>
		/// <value>The Spatial Reference System of the layer.</value>
		public ICoordinateSystem SRS
		{
			get { return srs; }
			set
			{
				if (value == null)
					throw new ArgumentNullException("value");
				srs = value;
				srsName = srs.Authority + ":" + srs.AuthorityCode;
				CheckSRS();
			}
		}

		/// <summary>
		/// The name of the srs.
		/// </summary>
		private string srsName = "EPSG:4326";

		/// <summary>
		/// Gets the name of the SRS.
		/// </summary>
		/// <value>The name of the SRS.</value>
		public string SRSName { get { return srsName; } }

		/// <summary>
		/// The image format to request.
		/// </summary>
		private string format = "image/png";

		/// <summary>
		/// Gets or sets the format.
		/// </summary>
		/// <value>The format.</value>
		public string Format
		{
			get { return format; }
			set
			{
				if (value == null)
					throw new ArgumentNullException("value");
				format = value;
			}
		}

		/// <summary>
		/// Set it to true to notify the WMSTileLayer to reload the capabilities.
		/// </summary>
		private bool baseURLChanged = false;

		/// <summary>
		/// The loader.
		/// </summary>
		private Coroutine loaderCoroutine;

		/// <summary>
		/// Set to true when the WMSTileLayer is parsing the capabilities.
		/// </summary>
		private bool isParsingGetCapabilities = false;

		/// <summary>
		/// The WMS capabilities.
		/// </summary>
		private UnitySlippyMap.WMS.WMT_MS_Capabilities capabilities;

		/// <summary>
		/// Gets the capabilities.
		/// </summary>
		/// <value>The capabilities.</value>
		public UnitySlippyMap.WMS.WMT_MS_Capabilities Capabilities { get { return capabilities; } }

        #endregion

        #region MonoBehaviour implementation

        /// <summary>
        /// Implementation of <see cref="http://docs.unity3d.com/ScriptReference/MonoBehaviour.html">MonoBehaviour</see>.Update().
        /// </summary>
        private void Update()
		{
			if (baseURLChanged && loaderCoroutine == null)
			{
				if (baseURL != null && baseURL != String.Empty)
				{
					loaderCoroutine = StartCoroutine(GetMap());
				}
				else
				{
					Debug.LogError("No base url has been set!");
				}
			}
		}

		private IEnumerator GetMap()
		{
			var url = baseURL + (baseURL.EndsWith("?") ? "" : "?") + "SERVICE=WMS&REQUEST=GetCapabilities&VERSION=1.1.1";

			using (var www = UnityWebRequest.Get(url))
			{
				yield return www.SendWebRequest();

				if (www.error != null || www.responseCode == 404 )
				{
#if DEBUG_LOG
					Debug.LogError ("ERROR: WMSTileLayer.Update: loader [" + www.url + "] error: " + www.error + "(" + www.downloadHandler.text + ")");
#endif
                    loaderCoroutine = null;
				}
				else
				{
					if (isParsingGetCapabilities == false)
					{
#if DEBUG_LOG
						Debug.Log ("DEBUG: WMSTileLayer.Update: GetCapabilities response:\n" + www.downloadHandler.text);
#endif

						byte[] bytes = www.downloadHandler.data;

						isParsingGetCapabilities = true;

						UnityThreadHelper.CreateThread(() =>
						{
							capabilities = null;
							try
							{
								XmlSerializer xs = new XmlSerializer(typeof(UnitySlippyMap.WMS.WMT_MS_Capabilities));
								using (XmlReader xr = XmlReader.Create(new MemoryStream(bytes),
								new XmlReaderSettings
								{
									DtdProcessing = DtdProcessing.Parse
#if UNITY_IPHONE || UNITY_ANDROID
									, XmlResolver = null
#endif
								}))
								{
									capabilities = xs.Deserialize(xr/*new MemoryStream(bytes)*/) as UnitySlippyMap.WMS.WMT_MS_Capabilities;
								}
							}
							catch (Exception
#if DEBUG_LOG
								e
#endif
							)
							{
#if DEBUG_LOG
								Debug.LogError ("ERROR: WMSTileLayer.Update: GetCapabilities deserialization exception:\n" + e.Source + " : " + e.InnerException + "\n" + e.Message + "\n" + e.StackTrace);
#endif
							}

#if DEBUG_LOG
							Debug.Log (String.Format (
                            "DEBUG: capabilities:\nversion: {0}\n" +
								"\tService:\n\t\tName: {1}\n\t\tTitle: {2}\n\t\tAbstract: {3}\n\t\tOnlineResource: {4}\n" + 
								"\t\tContactInformation:\n" +
								"\t\t\tContactAddress:\n\t\t\t\tAddressType: {5}\n\t\t\t\tAddress: {6}\n\t\t\t\tCity: {7}\n\t\t\t\tStateOrProvince: {8}\n\t\t\t\tPostCode: {9}\n\t\t\t\tCountry: {10}\n" +
								"\t\t\tContactElectronicMailAddress: {11}\n" +
								"\t\tFees: {12}\n",
                            capabilities.version,
                            capabilities.Service.Name,
                            capabilities.Service.Title,
                            capabilities.Service.Abstract,
                            capabilities.Service.OnlineResource.href,
                            capabilities.Service.ContactInformation.ContactAddress.AddressType,
                            capabilities.Service.ContactInformation.ContactAddress.Address,
                            capabilities.Service.ContactInformation.ContactAddress.City,
                            capabilities.Service.ContactInformation.ContactAddress.StateOrProvince,
                            capabilities.Service.ContactInformation.ContactAddress.PostCode,
                            capabilities.Service.ContactInformation.ContactAddress.Country,
                            capabilities.Service.ContactInformation.ContactElectronicMailAddress,
                            capabilities.Service.Fees
							));
#endif

							CheckLayers();
							CheckSRS();

							UnityThreadHelper.Dispatcher.Dispatch(() =>
							{
#if DEBUG_LOG
								if (capabilities != null) {
									string layers = String.Empty;
									foreach (UnitySlippyMap.WMS.Layer layer in capabilities.Capability.Layer.Layers) {
										layers += "'" + layer.Name + "': " + layer.Abstract + "\n";
									}
	
									Debug.Log ("DEBUG: WMSTileLayer.Update: layers: " + capabilities.Capability.Layer.Layers.Count + "\n" + layers);
								}
#endif

								isReadyToBeQueried = true;

                                loaderCoroutine = null;

								isParsingGetCapabilities = false;

								if (needsToBeUpdatedWhenReady)
								{
									UpdateContent();
									needsToBeUpdatedWhenReady = false;
								}
							});
						});
					}
				}
			}
		}
	
	#endregion
	
	#region TileLayer implementation
	
		/// <summary>
		/// Gets the numbers of tiles on each axis in respect to the map's zoom level. See <see cref="UnitySlippyMap.Layers.TileLayerBehaviour.GetTileCountPerAxis"/>.
		/// </summary>
		/// <param name="tileCountOnX">Tile count on x.</param>
		/// <param name="tileCountOnY">Tile count on y.</param>
		protected override void GetTileCountPerAxis (out int tileCountOnX, out int tileCountOnY)
		{
			tileCountOnX = tileCountOnY = (int)Mathf.Pow (2, Map.RoundedZoom);
		}
	
		/// <summary>
		/// Gets the tile coordinates and offsets to the origin for the tile under the center of the map. See <see cref="UnitySlippyMap.Layers.TileLayerBehaviour.GetCenterTile"/>.
		/// </summary>
		/// <param name="tileCountOnX">Tile count on x.</param>
		/// <param name="tileCountOnY">Tile count on y.</param>
		/// <param name="tileX">Tile x.</param>
		/// <param name="tileY">Tile y.</param>
		/// <param name="offsetX">Offset x.</param>
		/// <param name="offsetZ">Offset z.</param>
		protected override void GetCenterTile (int tileCountOnX, int tileCountOnY, out int tileX, out int tileY, out float offsetX, out float offsetZ)
		{
			int[] tileCoordinates = GeoHelpers.WGS84ToTile (Map.CenterWGS84 [0], Map.CenterWGS84 [1], Map.RoundedZoom);
			double[] centerTile = GeoHelpers.TileToWGS84 (tileCoordinates [0], tileCoordinates [1], Map.RoundedZoom);
			double[] centerTileMeters = Map.WGS84ToEPSG900913Transform.Transform (centerTile); //GeoHelpers.WGS84ToMeters(centerTile[0], centerTile[1]);

			tileX = tileCoordinates [0];
			tileY = tileCoordinates [1];
			offsetX = Map.RoundedHalfMapScale / 2.0f - (float)(Map.CenterEPSG900913 [0] - centerTileMeters [0]) * Map.RoundedScaleMultiplier;
			offsetZ = -Map.RoundedHalfMapScale / 2.0f - (float)(Map.CenterEPSG900913 [1] - centerTileMeters [1]) * Map.RoundedScaleMultiplier;
		}
	
		/// <summary>
		/// Gets the tile coordinates and offsets to the origin for the neighbour tile in the specified direction. See <see cref="UnitySlippyMap.Layers.TileLayerBehaviour.GetNeighbourTile"/>.
		/// </summary>
		/// <returns><c>true</c>, if neighbour tile was gotten, <c>false</c> otherwise.</returns>
		/// <param name="tileX">Tile x.</param>
		/// <param name="tileY">Tile y.</param>
		/// <param name="offsetX">Offset x.</param>
		/// <param name="offsetZ">Offset z.</param>
		/// <param name="tileCountOnX">Tile count on x.</param>
		/// <param name="tileCountOnY">Tile count on y.</param>
		/// <param name="dir">Dir.</param>
		/// <param name="nTileX">N tile x.</param>
		/// <param name="nTileY">N tile y.</param>
		/// <param name="nOffsetX">N offset x.</param>
		/// <param name="nOffsetZ">N offset z.</param>
		protected override bool GetNeighbourTile (int tileX, int tileY, float offsetX, float offsetZ, int tileCountOnX, int tileCountOnY, NeighbourTileDirection dir, out int nTileX, out int nTileY, out float nOffsetX, out float nOffsetZ)
		{
			bool ret = false;
			nTileX = 0;
			nTileY = 0;
			nOffsetX = 0.0f;
			nOffsetZ = 0.0f;
			
			switch (dir) {
			case NeighbourTileDirection.South:
				if ((tileY + 1) < tileCountOnY) {
					nTileX = tileX;
					nTileY = tileY + 1;
					nOffsetX = offsetX;
					nOffsetZ = offsetZ - Map.RoundedHalfMapScale;
					ret = true;
				}
				break;
			
			case NeighbourTileDirection.North:
				if (tileY > 0) {
					nTileX = tileX;
					nTileY = tileY - 1;
					nOffsetX = offsetX;
					nOffsetZ = offsetZ + Map.RoundedHalfMapScale;
					ret = true;
				}
				break;
			
			case NeighbourTileDirection.East:
				nTileX = tileX + 1;
				nTileY = tileY;
				nOffsetX = offsetX + Map.RoundedHalfMapScale;
				nOffsetZ = offsetZ;
				ret = true;
				break;
			
			case NeighbourTileDirection.West:
				nTileX = tileX - 1;
				nTileY = tileY;
				nOffsetX = offsetX - Map.RoundedHalfMapScale;
				nOffsetZ = offsetZ;
				ret = true;
				break;
			}
		

			return ret;
		}
	
	#endregion
	
	#region WebTileLayer implementation
	
		/// <summary>
		/// Gets the tile URL. See <see cref="UnitySlippyMap.Layers.TileLayerBehaviour.GetTileURL"/>.
		/// </summary>
		/// <returns>The tile UR.</returns>
		/// <param name="tileX">Tile x.</param>
		/// <param name="tileY">Tile y.</param>
		/// <param name="roundedZoom">Rounded zoom.</param>
		protected override string GetTileURL (int tileX, int tileY, int roundedZoom)
		{
			double[] tile = GeoHelpers.TileToWGS84 (tileX, tileY, roundedZoom);
			double[] tileMeters = Map.WGS84ToEPSG900913Transform.Transform (tile); //GeoHelpers.WGS84ToMeters(tile[0], tile[1]);
			float tileSize = Map.TileResolution * Map.RoundedMetersPerPixel;
			double[] min = Map.EPSG900913ToWGS84Transform.Transform (new double[2] {
				tileMeters [0],
				tileMeters [1] - tileSize
			}); //GeoHelpers.MetersToWGS84(xmin, ymin);
			double[] max = Map.EPSG900913ToWGS84Transform.Transform (new double[2] {
				tileMeters [0] + tileSize,
				tileMeters [1]
			}); //GeoHelpers.MetersToWGS84(xmax, ymax);
			return baseURL + (baseURL.EndsWith ("?") ? "" : "?") + "SERVICE=WMS&REQUEST=GetMap&VERSION=1.1.1&LAYERS=" + layers + "&STYLES=&SRS=" + srsName + "&BBOX=" + min [0] + "," + min [1] + "," + max [0] + "," + max [1] + "&WIDTH=" + Map.TileResolution + "&HEIGHT=" + Map.TileResolution + "&FORMAT=" + format;
		}
	#endregion

    #region WMSTileLayer implementation

		/// <summary>
		/// Throws an exception if the layers' list is invalid.
		/// </summary>
		private void CheckLayers ()
		{
			if (capabilities == null
				|| capabilities.Capability == null
				|| capabilities.Capability.Layer == null
				|| capabilities.Capability.Layer.Layers == null)
				return;

			// check if the layers exist
			string[] layersArray = layers.Split (new Char[] { ',' });
			foreach (string layersArrayItem in layersArray) {
				bool exists = false;
				foreach (UnitySlippyMap.WMS.Layer layer in capabilities.Capability.Layer.Layers) {
					if (layersArrayItem == layer.Name) {
						exists = true;
						break;
					}
				}
				if (exists == false) {
#if DEBUG_LOG
					Debug.LogError ("layer '" + layersArrayItem + "' doesn't exist");
#endif
					throw new ArgumentException ("layer '" + layersArrayItem + "' doesn't exist");
				}
			}
		}

		/// <summary>
		/// Throws an exception if the SRS is invalid.
		/// </summary>
		private void CheckSRS ()
		{
			if (capabilities == null
				|| capabilities.Capability == null
				|| capabilities.Capability.Layer == null
				|| capabilities.Capability.Layer.SRS == null)
				return;

			// check if the srs is supported
			bool exists = false;
			foreach (string supportedSRS in capabilities.Capability.Layer.SRS) {
				if (supportedSRS == srsName) {
					exists = true;
					break;
				}
			}
			if (exists == false)
				throw new ArgumentException ("SRS '" + srsName + "' isn't supported");
		}

    #endregion
	}

}