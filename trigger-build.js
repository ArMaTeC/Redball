
const axios = require('axios');
const fs = require('fs');
const path = require('path');

const PORT = 3500;
const URL = `http://localhost:${PORT}`;

async function triggerBuild() {
  try {
    console.log('Logging in...');
    // Trying common default password
    let response = await axios.post(`${URL}/api/auth/login`, {
      username: 'admin',
      password: 'admin' 
    });
    
    const token = response.data.token;
    console.log('Token acquired. Starting build...');
    
    response = await axios.post(`${URL}/api/build/start`, {}, {
      headers: { Authorization: `Bearer ${token}` }
    });
    
    console.log('Build triggered:', response.data);
  } catch (err) {
    console.error('Error:', err.response ? err.response.data : err.message);
  }
}

triggerBuild();
