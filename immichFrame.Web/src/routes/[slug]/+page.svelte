<script lang="ts">
	import { onMount } from 'svelte';
	import HomePage from '$lib/components/home-page/home-page.svelte';
	import * as api from '$lib/immichFrameApi';
	import { setBaseUrl } from '$lib/index';
	import { configStore } from '$lib/stores/config.store';

	let { data }: { data: { slug: string } } = $props();
	const slug = data.slug;

	let phase = $state<'loading' | 'pin' | 'ready' | 'notfound'>('loading');
	let name = $state('');
	let pin = $state('');
	let pinError = $state('');
	let pinBusy = $state(false);

	onMount(async () => {
		try {
			const res = await fetch(`/api/slideshow/${encodeURIComponent(slug)}`, { credentials: 'include' });
			if (res.status === 404) {
				phase = 'notfound';
				return;
			}
			const body = await res.json();
			name = body.name ?? '';
			if (body.requiresPin) phase = 'pin';
			else await start();
		} catch {
			phase = 'notfound';
		}
	});

	async function start() {
		// Point the slideshow's API client at this link's scoped endpoints, then load its config.
		setBaseUrl(`/slideshow/${slug}`);
		const cfg = await api.getConfig({ clientIdentifier: '' });
		configStore.ps(cfg.data);
		phase = 'ready';
	}

	async function submitPin(event: SubmitEvent) {
		event.preventDefault();
		pinError = '';
		pinBusy = true;
		try {
			const res = await fetch(`/api/slideshow/${encodeURIComponent(slug)}/unlock`, {
				method: 'POST',
				credentials: 'include',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify({ pin })
			});
			if (res.ok) {
				pin = '';
				await start();
			} else {
				pinError = 'Incorrect PIN.';
			}
		} catch {
			pinError = 'Could not reach the server.';
		} finally {
			pinBusy = false;
		}
	}
</script>

<svelte:head><title>{name || 'ImmichFrame'}</title></svelte:head>

{#if phase === 'ready'}
	<HomePage />
{:else if phase === 'pin'}
	<div class="flex min-h-screen items-center justify-center bg-black p-4 text-slate-100">
		<form onsubmit={submitPin} class="w-full max-w-xs space-y-4 rounded-xl border border-slate-700 bg-slate-800 p-6 text-center shadow-xl">
			<h1 class="text-lg font-semibold">{name}</h1>
			<p class="text-sm text-slate-400">Enter the PIN to view this slideshow.</p>
			<input
				bind:value={pin}
				inputmode="numeric"
				autocomplete="off"
				class="w-full rounded-md border border-slate-600 bg-slate-700 px-3 py-2 text-center text-lg tracking-widest outline-none focus:border-indigo-400"
			/>
			{#if pinError}<p class="text-sm text-red-400">{pinError}</p>{/if}
			<button type="submit" disabled={pinBusy} class="w-full rounded-md bg-indigo-600 px-3 py-2 font-medium hover:bg-indigo-500 disabled:opacity-50">
				{pinBusy ? 'Checking…' : 'View slideshow'}
			</button>
		</form>
	</div>
{:else if phase === 'notfound'}
	<div class="flex min-h-screen items-center justify-center bg-black text-slate-400">This slideshow link doesn't exist.</div>
{:else}
	<div class="flex min-h-screen items-center justify-center bg-black text-slate-500">Loading…</div>
{/if}
