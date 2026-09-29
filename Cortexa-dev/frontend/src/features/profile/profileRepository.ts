import type { FetchProfileResult, UpdateProfileResult, UserProfile } from './profileTypes';

const STUB_PROFILE: UserProfile = {
  name: 'Alpha User',
  email: 'alpha@cortexa.io',
  organization: 'Cortexa',
  theme: 'light',
  notifications: { batchCompletion: true, newOpportunities: true },
};

export async function fetchProfile(): Promise<FetchProfileResult> {
  await new Promise<void>((r) => setTimeout(r, 400));
  return { ok: true, data: { ...STUB_PROFILE } };
}

export async function updateProfile(patch: Partial<UserProfile>): Promise<UpdateProfileResult> {
  await new Promise<void>((r) => setTimeout(r, 600));
  return { ok: true, data: { ...STUB_PROFILE, ...patch } };
}
