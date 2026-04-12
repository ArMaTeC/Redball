import request from 'supertest';
import express from 'express';

// Setup mock express server mimicking the main update-server entrypoint
const app = express();
app.use(express.json());

app.get('/api/v1/update/check', (req, res) => {
  if (!req.query.version) return res.status(400).json({ error: 'Version required' });
  res.status(200).json({ updateAvailable: false, latestVersion: req.query.version });
});

describe('Update Server API Checks', () => {
  it('Should reject requests missing the version parameter', async () => {
    const res = await request(app).get('/api/v1/update/check');
    expect(res.statusCode).toEqual(400);
    expect(res.body).toHaveProperty('error', 'Version required');
  });

  it('Should return 200 OK with valid payloads', async () => {
    const res = await request(app).get('/api/v1/update/check?version=v3.0.0');
    expect(res.statusCode).toEqual(200);
    expect(res.body).toHaveProperty('updateAvailable', false);
    expect(res.body).toHaveProperty('latestVersion', 'v3.0.0');
  });
});
