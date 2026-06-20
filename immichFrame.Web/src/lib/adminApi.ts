// Lightweight client for the admin configuration API (cookie-authenticated, same-origin).
// Kept separate from the generated slideshow client (immichFrameApi.ts), which uses bearer auth.

export interface AdminUser {
	username: string;
}

export interface AuthResult {
	ok: boolean;
	username?: string;
	message?: string;
}

export type GeneralSettings = Record<string, unknown>;

async function readMessage(res: Response, fallback: string): Promise<string> {
	try {
		const data = (await res.json()) as { message?: string };
		return data?.message ?? fallback;
	} catch {
		return fallback;
	}
}

export async function getSetupRequired(): Promise<boolean> {
	const res = await fetch('/api/admin/setup-required', { credentials: 'include' });
	if (!res.ok) return false;
	const data = (await res.json()) as { setupRequired: boolean };
	return data.setupRequired;
}

export async function getMe(): Promise<AdminUser | null> {
	const res = await fetch('/api/admin/me', { credentials: 'include' });
	if (!res.ok) return null;
	return (await res.json()) as AdminUser;
}

async function postCredentials(path: string, username: string, password: string): Promise<AuthResult> {
	const res = await fetch(`/api/admin/${path}`, {
		method: 'POST',
		credentials: 'include',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify({ username, password })
	});
	if (res.ok) {
		const data = (await res.json()) as { username: string };
		return { ok: true, username: data.username };
	}
	return { ok: false, message: await readMessage(res, 'Request failed.') };
}

export const login = (username: string, password: string) => postCredentials('login', username, password);
export const setup = (username: string, password: string) => postCredentials('setup', username, password);

export async function logout(): Promise<void> {
	await fetch('/api/admin/logout', { method: 'POST', credentials: 'include' });
}

export async function getGeneralSettings(): Promise<GeneralSettings> {
	const res = await fetch('/api/admin/general', { credentials: 'include' });
	if (!res.ok) throw new Error(await readMessage(res, 'Failed to load settings.'));
	return (await res.json()) as GeneralSettings;
}

export async function saveGeneralSettings(settings: GeneralSettings): Promise<GeneralSettings> {
	const res = await fetch('/api/admin/general', {
		method: 'PUT',
		credentials: 'include',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify(settings)
	});
	if (!res.ok) throw new Error(await readMessage(res, 'Failed to save settings.'));
	return (await res.json()) as GeneralSettings;
}
