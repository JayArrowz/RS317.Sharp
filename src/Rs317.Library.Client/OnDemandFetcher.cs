using System;
using System.IO;
using System.IO.Compression;
using System.Net.Sockets;
using System.Threading;

namespace Rs317.Sharp
{

	public sealed class OnDemandFetcher : IRunnable
	{
		private int totalFiles;

		private DoubleEndedQueue requested;

		private int highestPriority;

		public String statusString;

		private int writeLoopCycle;

		private long lastRequestTime;

		private int[] mapIndices3;

		//private CRC32 crc32;

		private byte[] payload;

		public int onDemandCycle;

		private byte[][] filePriorities;

		private IBaseClient clientInstance;

		private DoubleEndedQueue passiveRequests;

		private int completedSize;

		private int expectedSize;

		private int[] musicPriorities;

		public int failedRequests;

		private int[] mapIndices2;

		private int filesLoaded;

		private bool running;

		private NetworkStream outputStream;

		private int[] mapIndices4;

		private bool waiting;

		private DoubleEndedQueue complete;

		private byte[] gzipInputBuffer;
		private int[] frames;
		private CacheableQueue nodeSubList;
		private NetworkStream inputStream;
		private TcpClient socket;
		private int[][] versions;
		private int[][] crcs;
		private int uncompletedCount;
		private int completedCount;
		private DoubleEndedQueue unrequested;
		private OnDemandData current;
		private DoubleEndedQueue mandatoryRequests;
		private int[] mapIndices1;
		private byte[] modelIndices;
		private int loopCycle;

		public OnDemandFetcher()
		{
			requested = new DoubleEndedQueue();
			statusString = "";
			//crc32 = new CRC32();
			payload = new byte[500];
			filePriorities = new byte[4][];
			passiveRequests = new DoubleEndedQueue();
			running = true;
			waiting = false;
			complete = new DoubleEndedQueue();
			gzipInputBuffer = new byte[65000];
			nodeSubList = new CacheableQueue();
			versions = new int[4][];
			crcs = new int[4][];
			unrequested = new DoubleEndedQueue();
			mandatoryRequests = new DoubleEndedQueue();
		}

		private void checkReceived()
		{
			OnDemandData request;
			lock(mandatoryRequests)
			{
				request = (OnDemandData)mandatoryRequests.popFront();
			}

			while(request != null)
			{
				waiting = true;
				byte[] data = null;
				if(clientInstance.caches[0] != null)
					data = clientInstance.caches[request.dataType + 1].decompress((int)request.id);
				if(!crcMatches(versions[request.dataType][request.id],
					crcs[request.dataType][request.id], data))
					data = null;
				lock(mandatoryRequests)
				{
					if(data == null)
					{
						unrequested.pushBack(request);
					}
					else
					{
						request.InitializeBuffer(data);
						lock(complete)
						{
							complete.pushBack(request);
						}
					}

					request = (OnDemandData)mandatoryRequests.popFront();
				}
			}
		}

		public void clearPassiveRequests()
		{
			lock(passiveRequests)
			{
				passiveRequests.clear();
			}
		}

		private void closeRequest(OnDemandData request)
		{
			try
			{
				if(socket == null)
				{
					long currentTime = TimeService.CurrentTimeInMilliseconds();
					if(currentTime - lastRequestTime < 4000L)
						return;
					lastRequestTime = currentTime;
					socket = clientInstance.openSocket(43594).ConfigureAwait(false).GetAwaiter().GetResult();
					inputStream = socket.GetStream();
					outputStream = socket.GetStream();
					outputStream.WriteByte(15);
					for(int j = 0; j < 8; j++)
						inputStream.ReadByte();

					loopCycle = 0;
				}

				payload[0] = (byte)request.dataType;
				payload[1] = (byte)(request.id >> 8);
				payload[2] = (byte)request.id;
				if(request.incomplete)
					payload[3] = 2;
				else if(!clientInstance.isLoggedIn)
					payload[3] = 1;
				else
					payload[3] = 0;
				outputStream.Write(payload, 0, 4);
				writeLoopCycle = 0;
				failedRequests = -10000;
				return;
			}
			catch(SocketException ioexception)
			{
			}

			try
			{
				socket.Close();
			}
			catch(Exception _ex)
			{
			}

			socket = null;
			inputStream = null;
			outputStream = null;
			expectedSize = 0;
			failedRequests++;
		}

		private bool crcMatches(int cacheVersion, int cacheChecksum, byte[] data)
		{
			if(data == null || data.Length < 2)
				return false;

			int length = data.Length - 2;
			int version = ((data[length] & 0xff) << 8) + (data[length + 1] & 0xff);
			/*crc32.reset();
			crc32.update(data, 0, length);
			int calculatedChecksum = (int)crc32.getValue();
			return version == cacheVersion && calculatedChecksum == cacheChecksum;*/
			return true;
		}

		public void disable()
		{
			running = false;
		}

		public int fileCount(int j)
		{
			return versions[j].Length;
		}

		public int getAnimCount()
		{
			return frames.Length;
		}

		public int getMapId(int type, int mapX, int mapY)
		{
			int coordinates = (mapX << 8) + mapY;
			for(int pointer = 0; pointer < mapIndices1.Length; pointer++)
				if(mapIndices1[pointer] == coordinates)
					if(type == 0)
						return mapIndices2[pointer];
					else
						return mapIndices3[pointer];
			return -1;
		}

		public int getModelId(int i)
		{
			return modelIndices[i] & 0xff;
		}

		public OnDemandData getNextNode()
		{
			OnDemandData onDemandData;
			lock(complete)
			{
				onDemandData = (OnDemandData)complete.popFront();
			}

			if(onDemandData == null)
				return null;
			lock(nodeSubList)
			{
				onDemandData.unlinkCacheable();
			}

			if(onDemandData.buffer == null)
				return onDemandData;
			int i = 0;
			try
			{
				GZipStream gzipinputstream = new GZipStream(new MemoryStream(onDemandData.buffer), CompressionMode.Decompress);
				do
				{
					if(i == gzipInputBuffer.Length)
						throw new Exception("buffer overflow!");
					int k = gzipinputstream.Read(gzipInputBuffer, i, gzipInputBuffer.Length - i);
					if(k == -1 || k == 0) //This was causing a hang here, different return value in C#. RS2Sharp made this change, but did not document it.
						break;
					i += k;
				} while(true);
			}
			catch(Exception _ex)
			{
				throw new Exception("error unzipping", _ex);
			}

			onDemandData.InitializeBuffer(new byte[i]);
			System.Buffer.BlockCopy(gzipInputBuffer, 0, onDemandData.buffer, 0, i);

			return onDemandData;
		}

		private void handleFailed()
		{
			uncompletedCount = 0;
			completedCount = 0;
			for(OnDemandData onDemandData = (OnDemandData)requested
					.peekFront();
				onDemandData != null;
				onDemandData = (OnDemandData)requested.getPrevious())
				if(onDemandData.incomplete)
					uncompletedCount++;
				else
					completedCount++;

			while(uncompletedCount < 10)
			{
				OnDemandData onDemandData_1 = (OnDemandData)unrequested.popFront();
				if(onDemandData_1 == null)
					break;
				if(filePriorities[onDemandData_1.dataType][onDemandData_1.id] != 0)
					filesLoaded++;
				filePriorities[onDemandData_1.dataType][onDemandData_1.id] = 0;
				requested.pushBack(onDemandData_1);
				uncompletedCount++;
				closeRequest(onDemandData_1);
				waiting = true;
			}
		}

		public int immediateRequestCount()
		{
			lock(nodeSubList)
			{
				return nodeSubList.getSize();
			}
		}

		public bool method564(int i)
		{
			for(int k = 0; k < mapIndices1.Length; k++)
				if(mapIndices3[k] == i)
					return true;
			return false;
		}

		private void method568()
		{
			while(uncompletedCount == 0 && completedCount < 10)
			{
				if(highestPriority == 0)
					break;
				OnDemandData onDemandData;
				lock(passiveRequests)
				{
					onDemandData = (OnDemandData)passiveRequests.popFront();
				}

				while(onDemandData != null)
				{
					if(filePriorities[onDemandData.dataType][onDemandData.id] != 0)
					{
						filePriorities[onDemandData.dataType][onDemandData.id] = 0;
						requested.pushBack(onDemandData);
						closeRequest(onDemandData);
						waiting = true;
						if(filesLoaded < totalFiles)
							filesLoaded++;
						statusString = "Loading extra files - " + (filesLoaded * 100) / totalFiles + "%";
						completedCount++;
						if(completedCount == 10)
							return;
					}

					lock(passiveRequests)
					{
						onDemandData = (OnDemandData)passiveRequests.popFront();
					}
				}

				for(int j = 0; j < 4; j++)
				{
					byte[] abyte0 = filePriorities[j];
					int k = abyte0.Length;
					for(int l = 0; l < k; l++)
						if(abyte0[l] == highestPriority)
						{
							abyte0[l] = 0;
							OnDemandData onDemandData_1 = new OnDemandData();
							onDemandData_1.dataType = j;
							onDemandData_1.id = l;
							onDemandData_1.incomplete = false;
							requested.pushBack(onDemandData_1);
							closeRequest(onDemandData_1);
							waiting = true;
							if(filesLoaded < totalFiles)
								filesLoaded++;
							statusString = "Loading extra files - " + (filesLoaded * 100) / totalFiles + "%";
							completedCount++;
							if(completedCount == 10)
								return;
						}

				}

				highestPriority--;
			}
		}

		public bool midiIdEqualsOne(int i)
		{
			return musicPriorities[i] == 1;
		}

		public void passiveRequest(int id, int type)
		{
			if(clientInstance.caches[0] == null)
				return;
			if(versions[type][id] == 0)
				return;
			if(filePriorities[type][id] == 0)
				return;
			if(highestPriority == 0)
				return;
			OnDemandData onDemandData = new OnDemandData();
			onDemandData.dataType = type;
			onDemandData.id = id;
			onDemandData.incomplete = false;
			lock(passiveRequests)
			{
				passiveRequests.pushBack(onDemandData);
			}
		}

		public void preloadRegions(bool flag)
		{
			int j = mapIndices1.Length;
			for(int k = 0; k < j; k++)
				if(flag || mapIndices4[k] != 0)
				{
					setPriority((byte)2, 3, mapIndices3[k]);
					setPriority((byte)2, 3, mapIndices2[k]);
				}
		}

		private void readData()
		{
			try
			{
				int j = socket.Available;
				if(expectedSize == 0 && j >= 6)
				{
					waiting = true;
					for(int k = 0; k < 6; k += inputStream.Read(payload, k, 6 - k))
						;
					int l = payload[0] & 0xff;
					int j1 = ((payload[1] & 0xff) << 8) + (payload[2] & 0xff);
					int l1 = ((payload[3] & 0xff) << 8) + (payload[4] & 0xff);
					int i2 = payload[5] & 0xff;
					current = null;
					for(OnDemandData onDemandData = (OnDemandData)requested
							.peekFront();
						onDemandData != null;
						onDemandData = (OnDemandData)requested.getPrevious())
					{
						if(onDemandData.dataType == l && onDemandData.id == j1)
							current = onDemandData;
						if(current != null)
							onDemandData.loopCycle = 0;
					}

					if(current != null)
					{
						loopCycle = 0;
						if(l1 == 0)
						{
							signlink.reporterror("Rej: " + l + "," + j1);
							current.ClearBuffer();
							if(current.incomplete)
								lock(complete)
								{
									complete.pushBack(current);
								}
							else
								current.unlink();

							current = null;
						}
						else
						{
							if(current.buffer == null && i2 == 0)
								current.InitializeBuffer(new byte[l1]);
							if(current.buffer == null && i2 != 0)
								throw new IOException("missing start of file");
						}
					}

					completedSize = i2 * 500;
					expectedSize = 500;
					if(expectedSize > l1 - i2 * 500)
						expectedSize = l1 - i2 * 500;
				}

				if(expectedSize > 0 && j >= expectedSize)
				{
					waiting = true;
					byte[] abyte0 = payload;
					int i1 = 0;
					if(current != null)
					{
						abyte0 = current.buffer;
						i1 = completedSize;
					}

					for(int k1 = 0; k1 < expectedSize; k1 += inputStream.Read(abyte0, k1 + i1, expectedSize - k1))
						;
					if(expectedSize + completedSize >= abyte0.Length && current != null)
					{
						if(clientInstance.caches[0] != null)
							clientInstance.caches[current.dataType + 1].put(abyte0.Length, abyte0, (int)current.id);
						if(!current.incomplete && current.dataType == 3)
						{
							current.incomplete = true;
							current.dataType = 93;
						}

						if(current.incomplete)
							lock(complete)
							{
								complete.pushBack(current);
							}
						else
							current.unlink();
					}

					expectedSize = 0;
				}
			}
			catch(IOException ioexception)
			{
				try
				{
					socket.Close();
				}
				catch(Exception _ex)
				{
				}

				socket = null;
				inputStream = null;
				outputStream = null;
				expectedSize = 0;
			}
		}

		public void request(int i)
		{
			request(0, i);
		}

		public void request(int i, int j)
		{
			if(i < 0 || i > versions.Length || j < 0 || j > versions[i].Length)
				throw new InvalidOperationException($"Failed to request map chunk: {i},{j}");

			if(versions[i][j] == 0)
				throw new InvalidOperationException($"Failed to request map chunk: {i},{j} Versions are 0");

			lock(nodeSubList)
			{
				for(OnDemandData onDemandData = (OnDemandData)nodeSubList
						.peek();
					onDemandData != null;
					onDemandData = (OnDemandData)nodeSubList
						.getNext())
					if(onDemandData.dataType == i && onDemandData.id == j)
						return;

				OnDemandData onDemandData_1 = new OnDemandData();
				onDemandData_1.dataType = i;
				onDemandData_1.id = j;
				onDemandData_1.incomplete = true;
				lock(mandatoryRequests)
				{
					mandatoryRequests.pushBack(onDemandData_1);
				}

				nodeSubList.push(onDemandData_1);
			}
		}

		public void run()
		{
			try
			{
				while(running)
				{
					onDemandCycle++;
					int i = 20;
					if(highestPriority == 0 && clientInstance.caches[0] != null)
						i = 50;
					try
					{
						Thread.Sleep(i);
					}
					catch(Exception _ex)
					{
					}

					waiting = true;
					for(int j = 0; j < 100; j++)
					{
						if(!waiting)
							break;
						waiting = false;
						checkReceived();
						handleFailed();
						if(uncompletedCount == 0 && j >= 5)
							break;
						method568();
						if(inputStream != null)
							readData();
					}

					bool flag = false;
					for(OnDemandData onDemandData = (OnDemandData)requested
							.peekFront();
						onDemandData != null;
						onDemandData = (OnDemandData)requested.getPrevious())
						if(onDemandData.incomplete)
						{
							flag = true;
							onDemandData.loopCycle++;
							if(onDemandData.loopCycle > 50)
							{
								onDemandData.loopCycle = 0;
								closeRequest(onDemandData);
							}
						}

					if(!flag)
					{
						for(OnDemandData onDemandData_1 = (OnDemandData)requested
								.peekFront();
							onDemandData_1 != null;
							onDemandData_1 = (OnDemandData)requested
								.getPrevious())
						{
							flag = true;
							onDemandData_1.loopCycle++;
							if(onDemandData_1.loopCycle > 50)
							{
								onDemandData_1.loopCycle = 0;
								closeRequest(onDemandData_1);
							}
						}

					}

					if(flag)
					{
						loopCycle++;
						if(loopCycle > 750)
						{
							try
							{
								socket.Close();
							}
							catch(Exception _ex)
							{
							}

							socket = null;
							inputStream = null;
							outputStream = null;
							expectedSize = 0;
						}
					}
					else
					{
						loopCycle = 0;
						statusString = "";
					}

					if(clientInstance.isLoggedIn && socket != null && outputStream != null
						&& (highestPriority > 0 || clientInstance.caches[0] == null))
					{
						writeLoopCycle++;
						if(writeLoopCycle > 500)
						{
							writeLoopCycle = 0;
							payload[0] = 0;
							payload[1] = 0;
							payload[2] = 0;
							payload[3] = 10;
							try
							{
								outputStream.Write(payload, 0, 4);
							}
							catch(IOException _ex)
							{
								loopCycle = 5000;
							}
						}
					}
				}
			}
			catch(Exception exception)
			{
				signlink.reporterror($"od_ex {exception.Message}\nStack: {exception.StackTrace}");
			}
		}

		public void setPriority(byte byte0, int i, int j)
		{
			if(clientInstance.caches[0] == null)
				return;
			if(versions[i][j] == 0)
				return;
			byte[] abyte0 = clientInstance.caches[i + 1].decompress(j);
			if(crcMatches(versions[i][j], crcs[i][j], abyte0))
				return;
			filePriorities[i][j] = byte0;
			if(byte0 > highestPriority)
				highestPriority = byte0;
			totalFiles++;
		}

		public void start(Archive streamLoader, IBaseClient client1)
		{
			String[] strings = { "model_version", "anim_version", "midi_version", "map_version" };
			for(int i = 0; i < 4; i++)
			{
				byte[] abyte0 = streamLoader.decompressFile(strings[i]);
				int j = abyte0.Length / 2;
				Default317Buffer stream = new Default317Buffer(abyte0);
				versions[i] = new int[j];
				filePriorities[i] = new byte[j];
				for(int l = 0; l < j; l++)
					versions[i][l] = stream.getUnsignedLEShort();

			}

			String[] strings2 = { "model_crc", "anim_crc", "midi_crc", "map_crc" };
			for(int k = 0; k < 4; k++)
			{
				byte[] abyte1 = streamLoader.decompressFile(strings2[k]);
				int i1 = abyte1.Length / 4;
				Default317Buffer stream_1 = new Default317Buffer(abyte1);
				crcs[k] = new int[i1];
				for(int l1 = 0; l1 < i1; l1++)
					crcs[k][l1] = stream_1.getInt();

			}

			byte[] abyte2 = streamLoader.decompressFile("model_index");
			int j1 = versions[0].Length;
			modelIndices = new byte[j1];
			for(int k1 = 0; k1 < j1; k1++)
				if(k1 < abyte2.Length)
					modelIndices[k1] = abyte2[k1];
				else
					modelIndices[k1] = 0;

			abyte2 = streamLoader.decompressFile("map_index");
			Default317Buffer stream2 = new Default317Buffer(abyte2);
			j1 = abyte2.Length / 7;
			mapIndices1 = new int[j1];
			mapIndices2 = new int[j1];
			mapIndices3 = new int[j1];
			mapIndices4 = new int[j1];
			for(int i2 = 0; i2 < j1; i2++)
			{
				mapIndices1[i2] = stream2.getUnsignedLEShort();
				mapIndices2[i2] = stream2.getUnsignedLEShort();
				mapIndices3[i2] = stream2.getUnsignedLEShort();
				mapIndices4[i2] = stream2.getUnsignedByte();
			}

			abyte2 = streamLoader.decompressFile("anim_index");
			stream2 = new Default317Buffer(abyte2);
			j1 = abyte2.Length / 2;
			frames = new int[j1];
			for(int j2 = 0; j2 < j1; j2++)
				frames[j2] = stream2.getUnsignedLEShort();

			abyte2 = streamLoader.decompressFile("midi_index");
			stream2 = new Default317Buffer(abyte2);
			j1 = abyte2.Length;
			musicPriorities = new int[j1];
			for(int k2 = 0; k2 < j1; k2++)
				musicPriorities[k2] = stream2.getUnsignedByte();

			clientInstance = client1;
			running = true;
			clientInstance.startRunnable(this, 2);
		}
	}
}
