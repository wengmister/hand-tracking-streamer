import socket

# Configure the server
HOST = '127.0.0.1'  # Standard loopback interface address (localhost)
PORT = 8000         # Port to listen on (must match the port in adb reverse and Unity)

print(f"--- Starting TCP server on {HOST}:{PORT} ---")
print("Waiting for a connection from the Quest...")

# Use a 'with' statement to ensure the socket is closed automatically
with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
    try:
        # Allow the port to be reused immediately after the script closes
        s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        
        # Bind the socket to the address and port
        s.bind((HOST, PORT))
        
        # Enable the server to accept connections (1 is the backlog size)
        s.listen(1)
        
        # Block and wait for an incoming connection
        conn, addr = s.accept()
        
        with conn:
            print(f"✔️  Connected by {addr}")
            print("--- Receiving data ---")
            
            # Buffer to hold incomplete messages
            buffer = ""
            while True:
                # Receive data from the client (up to 4096 bytes)
                data = conn.recv(4096)
                if not data:
                    # If data is empty, the client has closed the connection
                    break
                
                # Decode the byte string to a UTF-8 string and add to buffer
                buffer += data.decode('utf-8')
                
                # Process all complete messages in the buffer (split by newline)
                while '\n' in buffer:
                    message, buffer = buffer.split('\n', 1)
                    print(f"Received: {message}")

    except KeyboardInterrupt:
        print("\n--- Server is shutting down. ---")
    except Exception as e:
        print(f"An error occurred: {e}")
    finally:
        print("--- Connection closed. ---")