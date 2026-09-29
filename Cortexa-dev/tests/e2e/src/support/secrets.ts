import { DefaultAzureCredential } from '@azure/identity';
import { SecretClient } from '@azure/keyvault-secrets';

const secretCache = new Map<string, string>();

async function readKvSecret(vaultName: string, secretName: string): Promise<string> {
  const cacheKey = `${vaultName}/${secretName}`;
  const cached = secretCache.get(cacheKey);
  if (cached) {
    return cached;
  }

  const credential = new DefaultAzureCredential();
  const client = new SecretClient(`https://${vaultName}.vault.azure.net`, credential);
  const secret = await client.getSecret(secretName);

  if (!secret.value) {
    throw new Error(`Secret ${secretName} has no value`);
  }

  secretCache.set(cacheKey, secret.value);
  return secret.value;
}

export async function getAdminCredentials(
  vaultName: string,
  emailSecret: string,
  passwordSecret: string
): Promise<{ email: string; password: string }> {
  const emailEnv = process.env.CORTEXA_ADMIN_EMAIL;
  const passwordEnv = process.env.CORTEXA_ADMIN_PASSWORD;

  if (emailEnv && passwordEnv) {
    return { email: emailEnv, password: passwordEnv };
  }

  const [email, password] = await Promise.all([
    readKvSecret(vaultName, emailSecret),
    readKvSecret(vaultName, passwordSecret),
  ]);

  return { email, password };
}
